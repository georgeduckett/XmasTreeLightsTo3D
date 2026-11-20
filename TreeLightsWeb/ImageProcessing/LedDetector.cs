using System.Runtime.InteropServices;
using OpenCvSharp;

namespace TreeLightsWeb.ImageProcessing
{
    /// <summary>A bright blob in one of an LED's images: a candidate for where that LED appears in that view.</summary>
    public readonly record struct LedCandidate(int Led, int View, double U, double V, double Peak, int Area, double Flux);

    /// <summary>
    /// Finds the LED in an image of one lit LED. The tree with every LED off is estimated as the per-pixel median of a
    /// sample of the direction's images (each LED is lit in only one of them), scaled to the image's exposure, and
    /// subtracted. In what's left, each local maximum (strongest first) becomes a blob: the connected region above half its
    /// peak. The blob's brightness-weighted centroid is the position and its total brightness ('flux') ranks it, so the LED
    /// itself (or the glow around it) beats a small sparkle or reflection. The few brightest blobs are kept: which of them
    /// is the LED is decided later, by which ones the other views agree with.
    /// </summary>
    public sealed class LedDetector
    {
        public const int CandidatesPerImage = 4;
        private const int MaximaExamined = 16;
        private const int BackgroundSamples = 41;

        private readonly float[] _background;
        private readonly int _width, _height, _left, _right;
        private readonly double _sigma;
        private readonly int _halfWindow;
        private readonly int _dilateSize;

        /// <param name="background">The full-width background (see <see cref="BuildBackground"/>).</param>
        /// <param name="left">The first column to search.</param>
        /// <param name="right">One past the last column to search.</param>
        public LedDetector(float[] background, int width, int height, int left, int right)
        {
            _width = width;
            _height = height;
            _left = left;
            _right = right;
            var roiWidth = right - left;
            _background = new float[roiWidth * height];
            for (var y = 0; y < height; y++) Array.Copy(background, y * width + left, _background, y * roiWidth, roiWidth);
            // The image processing sizes were tuned on 1920x1080 images; scale them for other resolutions
            var scale = Math.Max(width, height) / 1920.0;
            _sigma = Math.Max(0.8, 2.0 * scale);
            _halfWindow = Math.Max(5, (int)Math.Round(45 * scale));
            _dilateSize = Math.Max(3, (int)Math.Round(15 * scale) | 1);
        }

        public int RoiWidth => _right - _left;

        /// <summary>Which of the direction's LED images to use for the background (spread evenly along the string).</summary>
        public static IReadOnlyList<int> BackgroundSampleIndices(int ledCount)
        {
            var count = Math.Min(BackgroundSamples, ledCount);
            if (count <= 1) return [0];
            var step = (ledCount - 1) / (double)(count - 1);
            return Enumerable.Range(0, count).Select(i => (int)(i * step)).Distinct().ToArray();
        }

        /// <summary>The per pixel median of the given images (grayscale).</summary>
        public static float[] BuildBackground(IReadOnlyList<string> imagePaths, int width, int height)
        {
            var images = new List<byte[]>();
            foreach (var path in imagePaths)
            {
                using var image = Cv2.ImRead(path, ImreadModes.Grayscale);
                if (image.Empty() || image.Width != width || image.Height != height) continue;
                var bytes = new byte[width * height];
                Marshal.Copy(image.Data, bytes, 0, bytes.Length); // (a freshly read image is continuous)
                images.Add(bytes);
            }
            if (images.Count == 0) throw new InvalidOperationException("None of the images for the background could be read.");

            var background = new float[width * height];
            var m = images.Count;
            Parallel.For(0, height, () => new byte[m], (y, _, values) =>
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    for (var j = 0; j < m; j++) values[j] = images[j][i];
                    Array.Sort(values);
                    background[i] = m % 2 == 1 ? values[m / 2] : 0.5f * (values[m / 2 - 1] + values[m / 2]);
                }
                return values;
            }, _ => { });
            return background;
        }

        /// <summary>
        /// Works out which columns of a direction's images to search, from where the LEDs were found (searching the whole
        /// width) in a sample of the images. The user's masks are used if nearly all of those LEDs are inside them
        /// (otherwise they're presumably out of date and are ignored); without masks, it's the columns where the LEDs were found,
        /// widened by a quarter each side. Returns the columns and a message to report (or null).
        /// </summary>
        public static (int Left, int Right, string? Message) ChooseColumns(IEnumerable<IReadOnlyList<LedCandidate>> sampleDetections, int width,
                                                                         int maskLeft, int maskRight)
        {
            var userLeft = Math.Clamp(maskLeft, 0, width - 1);
            var userRight = maskRight <= 0 || maskRight > width ? width : maskRight;
            var userMaskSet = (userLeft > 0 || userRight < width) && userRight > userLeft;

            // In each sample image, the brightest sharp blob is (almost always) the LED
            var positions = sampleDetections.Where(c => c.Count > 0)
                                            .Select(c => c.MaxBy(b => Math.Sqrt(Math.Max(b.Flux, 0) * Math.Max(b.Peak, 0))).U)
                                            .ToArray();
            if (positions.Length < 5)
            {
                return userMaskSet ? (userLeft, userRight, null) : (0, width, null);
            }
            if (userMaskSet)
            {
                var inside = positions.Count(u => u >= userLeft && u < userRight);
                if (inside >= 0.9 * positions.Length) return (userLeft, userRight, null);
            }
            var lo = LinearAlgebra.Percentile(positions.ToArray(), 5);
            var hi = LinearAlgebra.Percentile(positions.ToArray(), 95);
            var margin = Math.Max(0.25 * (hi - lo), 0.02 * width);
            var left = (int)Math.Max(0, Math.Floor(lo - margin));
            var right = (int)Math.Min(width, Math.Ceiling(hi + margin) + 1);
            var message = userMaskSet
                ? $"the left/right masks ({userLeft}-{userRight}) leave out a lot of the LEDs, so they've been ignored"
                : null;
            return (left, right, message);
        }

        /// <summary>Buffers for one thread's detections (so each image doesn't allocate tens of megabytes).</summary>
        public sealed class Workspace
        {
            internal Workspace(int size)
            {
                Image = new byte[size]; Values = new float[size]; Dilated = new float[size]; Taken = new bool[size]; Stamp = new int[size];
            }
            internal readonly byte[] Image;
            internal readonly float[] Values, Dilated;
            internal readonly bool[] Taken;
            internal readonly int[] Stamp;
            internal readonly List<double> Sample = [];
            internal readonly List<(float Value, int Index)> Maxima = [];
            internal readonly Stack<int> Stack = new();
        }

        public Workspace CreateWorkspace() => new(RoiWidth * _height);

        /// <summary>Finds the candidate blobs in one image. Returns an empty list if the image can't be read.</summary>
        public List<LedCandidate> Detect(string path, int led, int view, Workspace? workspace = null)
        {
            workspace ??= CreateWorkspace();
            var results = new List<LedCandidate>();
            using var full = Cv2.ImRead(path, ImreadModes.Grayscale);
            if (full.Empty() || full.Width != _width || full.Height != _height) return results;

            int w = RoiWidth, h = _height, n = w * h;
            var image = workspace.Image;
            using (var roi = new Mat(full, new Rect(_left, 0, w, h)))
            using (var copy = roi.Clone())
            {
                Marshal.Copy(copy.Data, image, 0, n);
            }

            // Exposure: how much brighter/darker this image is than the background (from the mid-tones)
            var sample = workspace.Sample;
            sample.Clear();
            for (var y = 0; y < h; y += 4)
            {
                for (var x = 0; x < w; x += 4)
                {
                    var b = _background[y * w + x];
                    if (b > 20 && b < 200) sample.Add(image[y * w + x] / b);
                }
            }
            var gain = sample.Count > 0 ? (float)LinearAlgebra.Median(CollectionsMarshal.AsSpan(sample)) : 1f;

            var d = workspace.Values;
            for (var i = 0; i < n; i++) d[i] = image[i] - gain * _background[i];

            var dilated = workspace.Dilated;
            using (var diff = new Mat(h, w, MatType.CV_32FC1))
            using (var blurred = new Mat())
            using (var max = new Mat())
            using (var kernel = Mat.Ones(_dilateSize, _dilateSize, MatType.CV_8UC1).ToMat())
            {
                Marshal.Copy(d, 0, diff.Data, n);
                Cv2.GaussianBlur(diff, blurred, new Size(0, 0), _sigma);
                Cv2.Dilate(blurred, max, kernel);
                // (both are newly allocated, so continuous)
                Marshal.Copy(blurred.Data, d, 0, n);
                Marshal.Copy(max.Data, dilated, 0, n);
            }

            // Noise level (robust standard deviation of a sample of the difference image) sets the detection threshold
            sample.Clear();
            for (var y = 0; y < h; y += 4)
                for (var x = 0; x < w; x += 4)
                    sample.Add(d[y * w + x]);
            var sampleSpan = CollectionsMarshal.AsSpan(sample);
            var median = LinearAlgebra.Median(sampleSpan);
            for (var i = 0; i < sampleSpan.Length; i++) sampleSpan[i] = Math.Abs(sampleSpan[i] - median);
            var noise = 1.4826 * LinearAlgebra.Median(sampleSpan);
            var threshold = Math.Max(6.0, 6.0 * noise);

            var maxima = workspace.Maxima;
            maxima.Clear();
            for (var i = 0; i < n; i++)
            {
                if (d[i] > threshold && d[i] >= dilated[i]) maxima.Add((d[i], i));
            }
            maxima.Sort((a, b) => b.Value.CompareTo(a.Value));

            var taken = workspace.Taken;
            var stamp = workspace.Stamp;
            Array.Clear(taken, 0, n);
            Array.Clear(stamp, 0, n);
            var stack = workspace.Stack;
            var blobs = new List<LedCandidate>();
            var blobId = 0;
            foreach (var (peak, index) in maxima.Take(MaximaExamined))
            {
                if (taken[index]) continue;
                blobId++;
                int py = index / w, px = index % w;
                int y0 = Math.Max(0, py - _halfWindow), y1 = Math.Min(h, py + _halfWindow + 1);
                int x0 = Math.Max(0, px - _halfWindow), x1 = Math.Min(w, px + _halfWindow + 1);
                var half = 0.5f * peak;
                double sw = 0, swx = 0, swy = 0, flux = 0;
                var area = 0;
                stack.Clear();
                stack.Push(index);
                stamp[index] = blobId;
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    int y = i / w, x = i % w;
                    var weight = d[i] - half;
                    sw += weight; swx += weight * x; swy += weight * y;
                    flux += d[i];
                    area++;
                    taken[i] = true;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var ny = y + dy;
                        if (ny < y0 || ny >= y1) continue;
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx;
                            if (nx < x0 || nx >= x1) continue;
                            var j = ny * w + nx;
                            if (stamp[j] != blobId && d[j] > half)
                            {
                                stamp[j] = blobId;
                                stack.Push(j);
                            }
                        }
                    }
                }
                blobs.Add(new LedCandidate(led, view, swx / sw + _left, swy / sw, peak, area, flux));
            }
            results.AddRange(blobs.OrderByDescending(b => b.Flux).Take(CandidatesPerImage));
            return results;
        }
    }
}
