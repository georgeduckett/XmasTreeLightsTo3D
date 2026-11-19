using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TreeLightsWeb.ImageCapture
{
    /// <summary>
    /// Looks after the captured image files in wwwroot/CapturedImages.
    ///
    /// A capture writes into a staging folder (CapturedImages/.staging/{direction}, which isn't served as a static file) and
    /// only once every image for the direction has arrived are they moved over the previous set. So an interrupted capture
    /// never leaves a mix of images from two different captures (it can be resumed instead). Image names are unchanged:
    /// {led}_{direction}.png, AllOn_{direction}.png and AllOff_{direction}.png.
    /// </summary>
    public sealed partial class CapturedImageStore
    {
        private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        private static readonly byte[] PngEnd = [0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82];
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly ILogger<CapturedImageStore> _logger;

        public string Root { get; }

        public CapturedImageStore(IWebHostEnvironment webHostEnvironment, ILogger<CapturedImageStore> logger)
        {
            Root = Path.Combine(webHostEnvironment.WebRootPath, "CapturedImages");
            _logger = logger;
        }

        public static string FileName(string shotName, int direction) => $"{shotName}_{direction.ToString(CultureInfo.InvariantCulture)}.png";

        private string StagingDirectory(int direction) => Path.Combine(Root, ".staging", direction.ToString(CultureInfo.InvariantCulture));
        private string StagingManifestPath(int direction) => Path.Combine(StagingDirectory(direction), "staging.json");
        private string ManifestPath(int direction) => Path.Combine(Root, $"capture_{direction.ToString(CultureInfo.InvariantCulture)}.json");
        private string UploadPath(int direction, string shotName, long seq) =>
            Path.Combine(StagingDirectory(direction), $"{FileName(shotName, direction)}.{seq.ToString(CultureInfo.InvariantCulture)}.upload");

        /// <summary>
        /// Gets the staging folder ready for a capture. When resuming, keeps what's there (if it was captured with the same
        /// LED count and resolution) and returns the names of the images that are already done; otherwise starts afresh.
        /// </summary>
        public IReadOnlySet<string> PrepareStaging(int direction, bool resume, StagingManifest manifest)
        {
            var directory = StagingDirectory(direction);
            if (resume && Directory.Exists(directory))
            {
                var existing = ReadJson<StagingManifest>(StagingManifestPath(direction));
                if (existing == null || existing.LedStart != manifest.LedStart || existing.LedCount != manifest.LedCount ||
                    existing.Width != manifest.Width || existing.Height != manifest.Height)
                {
                    throw new CaptureFailedException(existing == null
                        ? "Can't resume: the partial capture's details are missing. Start a new capture instead."
                        : $"Can't resume: the partial capture was taken with {existing.LedCount} LEDs at {existing.Width}×{existing.Height}, " +
                          $"but there are now {manifest.LedCount} LEDs and the camera is {manifest.Width}×{manifest.Height}. Start a new capture instead.");
                }

                var suffix = "_" + direction.ToString(CultureInfo.InvariantCulture) + ".png";
                var done = new HashSet<string>();
                foreach (var file in Directory.EnumerateFiles(directory, "*" + suffix))
                {
                    if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                        ReadPngSize(file, requireComplete: true) == (manifest.Width, manifest.Height))
                    {
                        var name = Path.GetFileName(file);
                        done.Add(name[..^suffix.Length]);
                    }
                }
                return done;
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
            Directory.CreateDirectory(directory);
            WriteJson(StagingManifestPath(direction), manifest);
            return new HashSet<string>();
        }

        /// <summary>
        /// Saves an image uploaded by the capture page into the staging folder (under a name that includes which frame
        /// request it was for), after checking it's a complete PNG. Returns the image's size.
        /// </summary>
        public async Task<(int Width, int Height)> WriteUploadAsync(int direction, string shotName, long seq, Stream content, CancellationToken ct)
        {
            var path = UploadPath(direction, shotName, seq);
            var tempPath = path + ".tmp";
            try
            {
                await using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
                {
                    var buffer = new byte[1 << 16];
                    while (true)
                    {
                        int read;
                        try
                        {
                            read = await content.ReadAsync(buffer, ct);
                        }
                        catch (IOException ex) when (!ct.IsCancellationRequested)
                        { // The connection dropped part way through: a problem with the upload, not with saving it
                            throw new InvalidDataException("The upload was interrupted.", ex);
                        }
                        if (read == 0) break;
                        await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }

                var size = ReadPngSize(tempPath, requireComplete: true)
                    ?? throw new InvalidDataException("The upload isn't a complete PNG image.");
                File.Move(tempPath, path, overwrite: true);
                return size;
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        public void DeleteUpload(int direction, string shotName, long seq) => TryDelete(UploadPath(direction, shotName, seq));

        public void DeleteStaged(int direction, string shotName) => TryDelete(Path.Combine(StagingDirectory(direction), FileName(shotName, direction)));

        /// <summary>
        /// If the upload for the given frame request has arrived, makes it the staged image for that shot.
        /// </summary>
        public bool TryPromoteUpload(int direction, string shotName, long seq)
        {
            var upload = UploadPath(direction, shotName, seq);
            if (!File.Exists(upload))
            {
                return false;
            }
            MoveWithRetries(upload, Path.Combine(StagingDirectory(direction), FileName(shotName, direction)));
            return true;
        }

        /// <summary>
        /// Replaces the direction's images with the staged ones, and removes the results image processing cached for the old
        /// images (otherwise processing would carry on using the old LED positions).
        /// </summary>
        public void Commit(int direction, IReadOnlyCollection<string> shotNames, CaptureManifest manifest)
        {
            var staging = StagingDirectory(direction);
            var missing = shotNames.FirstOrDefault(name => !File.Exists(Path.Combine(staging, FileName(name, direction))));
            if (missing != null)
            {
                throw new CaptureFailedException($"Image {FileName(missing, direction)} is missing from the staging folder.");
            }

            foreach (var name in shotNames)
            {
                var fileName = FileName(name, direction);
                MoveWithRetries(Path.Combine(staging, fileName), Path.Combine(Root, fileName));

                var stem = Path.Combine(Root, Path.GetFileNameWithoutExtension(fileName));
                TryDelete(stem + "_foundLoc.json");
                TryDelete(stem + "_foundLoc.png");
                TryDelete(stem + "_masked.png");
            }

            WriteJson(ManifestPath(direction), manifest);

            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Couldn't remove the staging folder {StagingFolder}", staging);
            }
        }

        /// <summary>
        /// Summarises what's been captured for each direction, including any partial (resumable) capture.
        /// </summary>
        public CaptureSummary GetSummary(bool treeConnected, int? ledStart, int? ledCount)
        {
            var directions = CaptureDirections.All;
            var counts = new int[directions.Count];
            var newest = new DateTime?[directions.Count];

            if (Directory.Exists(Root))
            {
                foreach (var file in new DirectoryInfo(Root).EnumerateFiles("*.png"))
                {
                    var match = LedImageRegex().Match(file.Name);
                    if (!match.Success ||
                        !int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var led) ||
                        !int.TryParse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var direction))
                    {
                        continue;
                    }
                    var index = IndexOf(directions, direction);
                    if (index < 0 || (ledCount != null && (led < ledStart || led >= ledStart + ledCount)))
                    {
                        continue;
                    }
                    counts[index]++;
                    if (newest[index] == null || file.LastWriteTimeUtc > newest[index])
                    {
                        newest[index] = file.LastWriteTimeUtc;
                    }
                }
            }

            var summaries = new List<DirectionSummary>(directions.Count);
            for (var i = 0; i < directions.Count; i++)
            {
                var direction = directions[i];
                var manifest = ReadJson<CaptureManifest>(ManifestPath(direction));
                var size = manifest != null ? (manifest.Width, manifest.Height) : counts[i] > 0 ? ExistingImageSize(direction) : null;

                var stagedImages = 0;
                var stagedTotal = 0;
                var staging = StagingDirectory(direction);
                if (Directory.Exists(staging))
                {
                    var suffix = "_" + direction.ToString(CultureInfo.InvariantCulture) + ".png";
                    stagedImages = Directory.EnumerateFiles(staging, "*" + suffix).Count(f => f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                    stagedTotal = (ReadJson<StagingManifest>(StagingManifestPath(direction))?.LedCount ?? 0) + 2;
                }

                summaries.Add(new DirectionSummary(direction, counts[i], ledCount != null && counts[i] >= ledCount,
                    manifest?.Completed ?? (newest[i] is DateTime newestUtc ? new DateTimeOffset(newestUtc, TimeSpan.Zero) : null),
                    size?.Item1, size?.Item2, stagedImages, stagedTotal));
            }

            return new CaptureSummary(treeConnected, ledCount, summaries);
        }

        /// <summary>
        /// The resolutions of the images already captured for the other directions (they all need to match).
        /// </summary>
        public IReadOnlySet<(int Width, int Height)> GetResolutionsOfOtherDirections(int direction)
        {
            var sizes = new HashSet<(int, int)>();
            foreach (var other in CaptureDirections.All.Where(d => d != direction))
            {
                var manifest = ReadJson<CaptureManifest>(ManifestPath(other));
                var size = manifest != null ? (manifest.Width, manifest.Height) : ExistingImageSize(other);
                if (size != null)
                {
                    sizes.Add(size.Value);
                }
            }
            return sizes;
        }

        private (int Width, int Height)? ExistingImageSize(int direction)
        {
            foreach (var name in new[] { "0", "AllOn" })
            {
                var size = ReadPngSize(Path.Combine(Root, FileName(name, direction)), requireComplete: false);
                if (size != null)
                {
                    return size;
                }
            }
            return null;
        }

        /// <summary>
        /// Reads a PNG's dimensions from its header. With <paramref name="requireComplete"/> it also checks the file ends with
        /// the PNG end marker, which catches truncated files. Returns null if it isn't a (complete) PNG.
        /// </summary>
        public static (int Width, int Height)? ReadPngSize(string path, bool requireComplete)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
                Span<byte> header = stackalloc byte[24];
                if (file.Length < 24 + PngEnd.Length || file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length ||
                    !header[..8].SequenceEqual(PngSignature) || !header[12..16].SequenceEqual("IHDR"u8))
                {
                    return null;
                }

                if (requireComplete)
                {
                    Span<byte> end = stackalloc byte[PngEnd.Length];
                    file.Seek(-PngEnd.Length, SeekOrigin.End);
                    if (file.ReadAtLeast(end, end.Length, throwOnEndOfStream: false) < end.Length || !end.SequenceEqual(PngEnd))
                    {
                        return null;
                    }
                }

                var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
                var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
                return width > 0 && height > 0 ? (width, height) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Moves a file over another, retrying briefly if it's locked (on Windows, antivirus or a file viewer can hold a new
        /// image open for a moment).
        /// </summary>
        private static void MoveWithRetries(string source, string destination)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(source, destination, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException &&
                                           ex is not FileNotFoundException and not DirectoryNotFoundException && attempt < 6)
                {
                    Thread.Sleep(100 * attempt);
                }
            }
        }

        private static int IndexOf(IReadOnlyList<int> list, int value)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return i;
            }
            return -1;
        }

        private T? ReadJson<T>(string path) where T : class
        {
            try
            {
                return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Couldn't read {Path}", path);
                return null;
            }
        }

        private static void WriteJson<T>(string path, T value)
        {
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }

        private void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Couldn't delete {Path}", path);
            }
        }

        [GeneratedRegex(@"^(\d{1,9})_(\d{1,3})\.png$", RegexOptions.CultureInvariant)]
        private static partial Regex LedImageRegex();
    }
}
