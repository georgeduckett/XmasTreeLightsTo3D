using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using OpenCvSharp;
using TreeLightsWeb.ImageCapture;
using TreeLightsWeb.Models;

namespace TreeLightsWeb.ImageProcessing
{
    /// <summary>
    /// Turns the captured images (one per LED per direction) into the LEDs' 3D coordinates (Config/coordinates.csv).
    ///
    /// 1. <see cref="LedDetector"/> finds the few brightest blobs in each image (the results are cached per direction in
    ///    CapturedImages/.processing and reused while the images, and the columns searched, are unchanged).
    /// 2. <see cref="TreeReconstructor"/> works out the camera, the actual angles the tree was turned by, and each LED's
    ///    3D position from the blobs that the views agree on, using the wire (neighbouring LEDs are close) to reject
    ///    wrong matches.
    /// 3. The positions are written in GIFT coordinates (x and y from -1 to 1 about the trunk, z up from 0 at the lowest
    ///    LED, all on the same scale), plus a _foundLoc.json per image for the coordinate correction page.
    /// </summary>
    public class ImageProcessor
    {
        private const int CacheVersion = 1;

        private readonly ImageProcessingModel _model;
        private readonly string _imageFolder;
        private readonly IReadOnlyList<int> _directions = CaptureDirections.All;

        public ImageProcessor(ImageProcessingModel model)
        {
            _model = model;
            _imageFolder = Path.Combine(model.WebRootFolder!, "CapturedImages");
        }

        public static MinMaxLocResult? GetMinMaxLoc(string imageFilePath)
        {
            var jsonPath = imageFilePath[..imageFilePath.LastIndexOf('.')] + "_foundLoc.json";
            return File.Exists(jsonPath) ? JsonConvert.DeserializeObject<MinMaxLocResult>(File.ReadAllText(jsonPath)) : null;
        }

        private string ImagePath(int led, int direction) => Path.Combine(_imageFolder, CapturedImageStore.FileName(led.ToString(CultureInfo.InvariantCulture), direction));
        private string ShotPath(string name, int direction) => Path.Combine(_imageFolder, CapturedImageStore.FileName(name, direction));
        private string CachePath(int direction) => Path.Combine(_imageFolder, ".processing", $"candidates_{direction.ToString(CultureInfo.InvariantCulture)}.json");

        /// <summary>
        /// Runs the processing. Progress messages starting with \r replace the previous line; others are new lines.
        /// </summary>
        public async Task ProcessImages(Func<string, Task> updateFunc)
        {
            // The work is CPU bound and runs on the thread pool; its messages are forwarded from here
            var lines = new ConcurrentQueue<string>();
            string? status = null, lastStatus = null;
            var work = Task.Run(() => Process(s => status = s, s => { lines.Enqueue(s); status = null; }));
            while (true)
            {
                var finished = await Task.WhenAny(work, Task.Delay(500)) == work;
                while (lines.TryDequeue(out var line))
                {
                    // A finished step's line replaces the progress line that was showing for it
                    await updateFunc((lastStatus != null ? "\r" : string.Empty) + line);
                    lastStatus = null;
                }
                var current = status;
                if (current != null && current != lastStatus)
                {
                    await updateFunc((lastStatus == null ? string.Empty : "\r") + current);
                    lastStatus = current;
                }
                if (finished) break;
            }
            await work; // rethrows any error
        }

        private void Process(Action<string> status, Action<string> line)
        {
            var total = Stopwatch.StartNew();
            var ledStart = _model.LEDStart;
            var ledCount = _model.LEDCount;
            if (ledCount <= 0) throw new InvalidOperationException("The number of LEDs isn't known: is the tree (WLED) connected?");

            // Image size, and check the images are there
            var (width, height) = ImageSize(ledStart);
            var missing = new int[_directions.Count];
            for (var d = 0; d < _directions.Count; d++)
            {
                for (var i = 0; i < ledCount; i++) if (!File.Exists(ImagePath(ledStart + i, _directions[d]))) missing[d]++;
                if (missing[d] > ledCount / 10)
                {
                    throw new InvalidOperationException($"{missing[d]} of the {ledCount} LED images for the {_directions[d]}° direction are missing: capture that direction first.");
                }
            }
            line($"Processing {ledCount} LEDs x {_directions.Count} directions ({width}x{height} images)" +
                 (missing.Sum() > 0 ? $", {missing.Sum()} images missing" : string.Empty));

            // 1. Find the LEDs in the images
            var candidates = new List<LedCandidate>();
            for (var d = 0; d < _directions.Count; d++)
            {
                candidates.AddRange(DetectDirection(d, ledStart, ledCount, width, height, status, line));
            }

            // 2. Work out the 3D positions
            var reconstructor = new TreeReconstructor(ledCount, _directions.Count, width, height, candidates, status);
            var result = reconstructor.Run();
            Report(result, line);

            // 3. Write the results
            status("Writing the coordinates");
            WriteCoordinates(result);
            WriteFoundLocations(result, ledStart, width, height);

            var confirmed = result.ViewsUsed.Count(v => v > 0);
            line($"Done in {total.Elapsed.TotalSeconds:0}s: {confirmed} of {ledCount} LED positions confirmed by the images" +
                 (confirmed < ledCount ? $", {ledCount - confirmed} estimated from their neighbours" : string.Empty) +
                 $"; {result.FarFromNeighbours} further from both neighbours than the wire allows.");
        }

        private (int Width, int Height) ImageSize(int ledStart)
        {
            var sizes = new HashSet<(int, int)>();
            foreach (var direction in _directions)
            {
                var size = CapturedImageStore.ReadPngSize(ImagePath(ledStart, direction), requireComplete: false)
                           ?? CapturedImageStore.ReadPngSize(ShotPath("AllOff", direction), requireComplete: false);
                if (size == null) throw new InvalidOperationException($"There are no images for the {direction}° direction: capture it first.");
                sizes.Add(size.Value);
            }
            if (sizes.Count > 1)
            {
                throw new InvalidOperationException($"The directions were captured at different resolutions ({string.Join(", ", sizes.Select(s => $"{s.Item1}x{s.Item2}"))}); they need to be the same.");
            }
            return sizes.Single();
        }

        // ------------------------------------------------------------------------------------------------- detection

        private sealed record CandidateCache(int Version, int Direction, int LedStart, int LedCount, int Width, int Height, int MaskLeft, int MaskRight,
                                             string Images, int Left, int Right, List<double[]> Candidates);

        private List<LedCandidate> DetectDirection(int view, int ledStart, int ledCount, int width, int height, Action<string> status, Action<string> line)
        {
            var direction = _directions[view];
            var mask = _model.ImageMaskingModels != null && view < _model.ImageMaskingModels.Length ? _model.ImageMaskingModels[view] : null;
            int maskLeft = mask?.LeftBlank ?? 0, maskRight = mask?.RightBlank ?? 0;

            status($"Checking for changes to the {direction}° images");
            var signature = ImagesSignature(direction, ledStart, ledCount);
            var cachePath = CachePath(direction);
            if (!_model.RecalculateImageLEDCoordinates && File.Exists(cachePath))
            {
                try
                {
                    var cache = System.Text.Json.JsonSerializer.Deserialize<CandidateCache>(File.ReadAllText(cachePath));
                    if (cache != null && cache.Version == CacheVersion && cache.Direction == direction && cache.LedStart == ledStart &&
                        cache.LedCount == ledCount && cache.Width == width && cache.Height == height && cache.MaskLeft == maskLeft &&
                        cache.MaskRight == maskRight && cache.Images == signature)
                    {
                        line($"{direction}°: using the LEDs found last time (the images haven't changed)");
                        return cache.Candidates.Select(c => new LedCandidate((int)c[0], view, c[1], c[2], c[3], (int)c[4], c[5])).ToList();
                    }
                }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or NotSupportedException)
                {
                    // Find them again
                }
            }

            var stopwatch = Stopwatch.StartNew();
            var threads = Cv2.GetNumThreads();
            Cv2.SetNumThreads(1); // parallelise over images instead
            try
            {
                // The background (the tree with every LED off), and where in the images the LEDs are
                status($"Finding the LEDs in the {direction}° images: estimating the background");
                var samples = LedDetector.BackgroundSampleIndices(ledCount).Select(i => (Led: i, Path: ImagePath(ledStart + i, direction)))
                                         .Where(s => File.Exists(s.Path)).ToArray();
                var background = LedDetector.BuildBackground(samples.Select(s => s.Path).ToArray(), width, height);
                var probe = new LedDetector(background, width, height, 0, width);
                var sampleDetections = new IReadOnlyList<LedCandidate>[samples.Length];
                Parallel.For(0, samples.Length, i => sampleDetections[i] = probe.Detect(samples[i].Path, samples[i].Led, view));
                var (left, right, maskMessage) = LedDetector.ChooseColumns(sampleDetections, width, maskLeft, maskRight);
                if (maskMessage != null) line($"{direction}°: {maskMessage}");

                var detector = new LedDetector(background, width, height, left, right);
                var results = new List<LedCandidate>[ledCount];
                var done = 0;
                Parallel.For(0, ledCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, detector.CreateWorkspace, (i, _, workspace) =>
                {
                    var path = ImagePath(ledStart + i, direction);
                    results[i] = File.Exists(path) ? detector.Detect(path, i, view, workspace) : [];
                    var count = Interlocked.Increment(ref done);
                    if (count % 25 == 0) status($"Finding the LEDs in the {direction}° images: {count} of {ledCount}");
                    return workspace;
                }, _ => { });

                var candidates = results.SelectMany(r => r).ToList();
                var unseen = results.Count(r => r.Count == 0);
                line($"{direction}°: found the LEDs in {ledCount} images in {stopwatch.Elapsed.TotalSeconds:0}s (searching columns {left}-{right})" +
                     (unseen > 0 ? $"; nothing found in {unseen}" : string.Empty));

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    var cache = new CandidateCache(CacheVersion, direction, ledStart, ledCount, width, height, maskLeft, maskRight, signature, left, right,
                                                   candidates.Select(c => new[] { c.Led, Math.Round(c.U, 3), Math.Round(c.V, 3), Math.Round(c.Peak, 2), c.Area, Math.Round(c.Flux) }).ToList());
                    File.WriteAllText(cachePath + ".tmp", System.Text.Json.JsonSerializer.Serialize(cache));
                    File.Move(cachePath + ".tmp", cachePath, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    line($"(Couldn't save the LEDs found in the {direction}° images, so they'll be found again next time: {ex.Message})");
                }
                return candidates;
            }
            finally
            {
                Cv2.SetNumThreads(threads);
            }
        }

        /// <summary>Identifies the exact set of images (names, sizes and modification times) a direction's results came from.</summary>
        private string ImagesSignature(int direction, int ledStart, int ledCount)
        {
            var text = new StringBuilder();
            void Add(string path)
            {
                var info = new FileInfo(path);
                text.Append(Path.GetFileName(path)).Append(':');
                if (info.Exists) text.Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
                text.Append(';');
            }
            Add(ShotPath("AllOn", direction));
            Add(ShotPath("AllOff", direction));
            for (var i = 0; i < ledCount; i++) Add(ImagePath(ledStart + i, direction));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        // -------------------------------------------------------------------------------------------------- results

        private void Report(ReconstructionResult result, Action<string> line)
        {
            var camera = result.Camera;
            var angles = Enumerable.Range(0, camera.Views).Select(k => camera.Angle(k) * 180 / Math.PI).ToArray();
            line("Measured direction of each capture: " + string.Join(", ", angles.Select(a => $"{a:0}°")) +
                 $" (turned {(camera.Sign < 0 ? "clockwise" : "anticlockwise")} seen from above" +
                 (result.DirectionFromBrightness ? ", from which side of the tree the LEDs look brightest)" : ", from the perspective)"));
            var fov = 2 * Math.Atan(camera.Width / 2.0 / camera.FocalLength) * 180 / Math.PI;
            line($"Camera: {fov:0}° horizontal field of view, looking {Math.Abs(camera.Pitch * 180 / Math.PI):0.0}° {(camera.Pitch >= 0 ? "down" : "up")}, " +
                 $"tilted {camera.Roll * 180 / Math.PI:0.0}°");

            var views = result.ViewsUsed;
            var n = views.Length;
            line($"LED positions: {views.Count(v => v >= 3)} confirmed by 3 or more views, {views.Count(v => v == 2)} by 2, " +
                 $"{views.Count(v => v == 0)} estimated from their neighbours along the wire" +
                 (views.Any(v => v == 0) ? $" ({string.Join(", ", Enumerable.Range(0, n).Where(i => views[i] == 0))})" : string.Empty));
            if (result.MatchedFloorOrCeiling.Length > 0)
            {
                var where = result.Floor != null && result.Ceiling != null ? "floor and ceiling" : result.Ceiling != null ? "ceiling" : "floor";
                line($"Ignored the light the LEDs cast on the {where} (and things around the bottom of the tree): it was the best match in the images for {result.MatchedFloorOrCeiling.Length} LEDs");
            }
            var corrected = Enumerable.Range(0, n).Where(i => result.WireCorrected[i]).ToArray();
            if (corrected.Length > 0)
            {
                line($"{corrected.Length} LEDs aren't where the images alone would put them (because of the above, or the wire ruled out the best match): " +
                     string.Join(", ", corrected));
            }

            var errors = result.LeaveOneOutErrors;
            if (errors.Length > 0)
            {
                var zs = result.Positions.Select(p => p.Z).ToArray();
                var heightPixels = (zs.Max() - zs.Min()) * camera.FocalLength;
                var median = LinearAlgebra.Median(errors.ToArray());
                var p90 = LinearAlgebra.Percentile(errors.ToArray(), 90);
                line($"Accuracy check (each view's sighting of an LED predicted from the other views): median error {median:0.0} px, 90% within {p90:0.0} px; " +
                     $"the tree is about {heightPixels:0} px tall, so that's {100 * median / heightPixels:0.0}% of its height");
            }
        }

        /// <summary>GIFT coordinates: about the trunk, x and y scaled to -1..1 and z from 0, all on the same scale.</summary>
        private static Vec3[] ToGift(IReadOnlyList<Vec3> positions, IReadOnlyList<Vec3> reference)
        {
            var radius = reference.Max(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
            var bottom = reference.Min(p => p.Z);
            return positions.Select(p => new Vec3(p.X / radius, p.Y / radius, (p.Z - bottom) / radius)).ToArray();
        }

        private void WriteCoordinates(ReconstructionResult result)
        {
            var gift = ToGift(result.Positions, result.Positions);
            // The positions the images alone would give, on the same scale (x100, for the 'original positions' view)
            var original = ToGift(result.ImageOnlyPositions.Select((p, i) => p ?? result.Positions[i]).ToArray(), result.Positions);
            static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

            var lines = new List<string> { "index, x, y, z, r, theta, equdelta, wascorrected, original x, original y, original z" };
            for (var i = 0; i < gift.Length; i++)
            {
                var p = gift[i];
                var corrected = result.WireCorrected[i] || result.ViewsUsed[i] == 0;
                lines.Add($"{i}, {F(p.X)}, {F(p.Y)}, {F(p.Z)}, {F(Math.Sqrt(p.X * p.X + p.Y * p.Y))}, {F(Math.Atan2(p.Y, p.X))}, " +
                          $"{F(result.RmsError[i])}, {(corrected ? "True" : "False")}, {F(100 * original[i].X)}, {F(100 * original[i].Y)}, {F(100 * original[i].Z)}");
            }
            var path = Path.Combine(_model.WebRootFolder!, "Config", "coordinates.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path + ".tmp", lines);
            File.Move(path + ".tmp", path, overwrite: true);
        }

        /// <summary>
        /// For the coordinate correction page: per image, where the LED's final position projects (MaxLoc; MaxVal is 1 if
        /// that view confirmed it, 0 if it's only where the LED should be) and the blob used (MinLoc).
        /// </summary>
        private void WriteFoundLocations(ReconstructionResult result, int ledStart, int width, int height)
        {
            var size = new OpenCvSharp.Point(width, height);
            Parallel.For(0, result.Positions.Length, i =>
            {
                for (var k = 0; k < _directions.Count; k++)
                {
                    result.Camera.Project(result.Positions[i], k, out var u, out var v);
                    var projected = new OpenCvSharp.Point((int)Math.Round(Math.Clamp(u, -1e6, 1e6)), (int)Math.Round(Math.Clamp(v, -1e6, 1e6)));
                    var used = result.UsedCandidates[i][k];
                    var found = used is { } c ? new OpenCvSharp.Point((int)Math.Round(c.U), (int)Math.Round(c.V)) : projected;
                    var json = JsonConvert.SerializeObject(new MinMaxLocResult(result.RmsError[i], used != null ? 1 : 0, found, projected, size));
                    var path = ImagePath(ledStart + i, _directions[k]);
                    File.WriteAllText(path[..path.LastIndexOf('.')] + "_foundLoc.json", json);
                }
            });
        }
    }
}
