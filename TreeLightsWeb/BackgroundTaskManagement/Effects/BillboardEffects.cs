using System.Runtime.InteropServices;
using OpenCvSharp;
using static TreeLightsWeb.BackgroundTaskManagement.Effects.EffectMaths;

namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    /// <summary>
    /// The tree seen from one side: where each LED appears across the view (u, in tree units from the trunk; up is just z),
    /// and how much it should be used (1 on the near side of the tree, fading to 0 round the sides, so the back of the tree,
    /// which would show the picture back to front, stays off).
    /// </summary>
    public sealed class FrontView
    {
        /// <param name="orientation">
        /// Which side (0 is the side that faced the camera in the first captured image, and the angle goes anticlockwise
        /// round the tree, seen from above), and whether to flip left and right (if the LED positions are a mirror image).
        /// </param>
        public FrontView(TreeGeometry tree, TreeOrientation orientation)
        {
            var mirror = orientation.Mirrored;
            // The capture camera is at -y looking along +y, with +x to its right
            var viewer = (-90 + orientation.FrontDirection) * MathF.PI / 180;
            float towardsX = MathF.Cos(viewer), towardsY = MathF.Sin(viewer); // from the trunk towards the viewer
            float rightX = -towardsY, rightY = towardsX;                       // the viewer's right
            U = new float[tree.Count];
            Weight = new float[tree.Count];
            for (var i = 0; i < tree.Count; i++)
            {
                U[i] = (tree.X[i] * rightX + tree.Y[i] * rightY) * (mirror ? -1 : 1);
                Weight[i] = SmoothStep(-0.12f, 0.12f, tree.X[i] * towardsX + tree.Y[i] * towardsY);
            }
        }

        public float[] U { get; }
        public float[] Weight { get; }
    }

    /// <summary>An RGB image (0-1) placed in front of the tree: its top left corner at (left, top) in tree units.</summary>
    public sealed class Picture
    {
        private readonly float[] _rgb;

        private Picture(float[] rgb, int width, int height, float left, float top, float pixelsPerUnit)
        {
            _rgb = rgb; Width = width; Height = height; Left = left; Top = top; PixelsPerUnit = pixelsPerUnit;
        }

        public int Width { get; }
        public int Height { get; }
        public float Left { get; set; }
        public float Top { get; }
        public float PixelsPerUnit { get; }
        public float WidthInUnits => Width / PixelsPerUnit;

        /// <summary>From an 8 bit BGR (or greyscale) image.</summary>
        public static Picture FromMat(Mat image, float left, float top, float pixelsPerUnit)
        {
            using var bgr = new Mat();
            if (image.Channels() == 1) Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR); else image.CopyTo(bgr);
            var bytes = new byte[bgr.Width * bgr.Height * 3];
            Marshal.Copy(bgr.Data, bytes, 0, bytes.Length); // (a new Mat is continuous)
            var rgb = new float[bytes.Length];
            for (var p = 0; p < bytes.Length; p += 3)
            {
                rgb[p] = bytes[p + 2] / 255f;
                rgb[p + 1] = bytes[p + 1] / 255f;
                rgb[p + 2] = bytes[p] / 255f;
            }
            return new Picture(rgb, bgr.Width, bgr.Height, left, top, pixelsPerUnit);
        }

        /// <summary>The colour at (u, v) in tree units (black outside the picture), interpolated between pixels.</summary>
        public Rgb Sample(float u, float v)
        {
            var x = (u - Left) * PixelsPerUnit - 0.5f;
            var y = (Top - v) * PixelsPerUnit - 0.5f;
            if (x < -1 || y < -1 || x > Width || y > Height) return Rgb.Black;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            return Rgb.Lerp(Rgb.Lerp(Pixel(x0, y0), Pixel(x0 + 1, y0), fx), Rgb.Lerp(Pixel(x0, y0 + 1), Pixel(x0 + 1, y0 + 1), fx), fy);
        }

        private Rgb Pixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return Rgb.Black;
            var p = (y * Width + x) * 3;
            return new Rgb(_rgb[p], _rgb[p + 1], _rgb[p + 2]);
        }
    }

    public static class TextPicture
    {
        /// <summary>
        /// Text in bold capitals, white on black, with letters <paramref name="letterHeight"/> tree units tall sitting on
        /// height <paramref name="baseline"/>, starting at u = 0.
        /// </summary>
        public static Picture Create(string text, float letterHeight, float baseline, float pixelsPerUnit)
        {
            const HersheyFonts font = HersheyFonts.HersheyDuplex;
            var letterPixels = letterHeight * pixelsPerUnit;
            var unitSize = Cv2.GetTextSize("H", font, 1, 1, out _);
            var scale = letterPixels / unitSize.Height;
            var thickness = Math.Max(2, (int)Math.Round(letterPixels * 0.17)); // bold, so the strokes are a couple of LEDs wide
            var size = Cv2.GetTextSize(text, font, scale, thickness, out _);
            var margin = thickness;
            using var image = new Mat((int)(letterPixels + 2 * margin), size.Width + 2 * margin, MatType.CV_8UC1, Scalar.Black);
            Cv2.PutText(image, text, new Point(margin, margin + (int)letterPixels), font, scale, Scalar.White, thickness, LineTypes.AntiAlias);
            Cv2.GaussianBlur(image, image, new Size(0, 0), pixelsPerUnit * 0.025);
            return Picture.FromMat(image, 0, baseline + (image.Height - margin) / pixelsPerUnit, pixelsPerUnit);
        }
    }

    /// <summary>
    /// A message scrolling across the front of the tree (the side facing the room), in big letters (about one fits on the
    /// tree at a time), in the palette's colours (or its own "Letters" colour).
    /// </summary>
    public sealed class ScrollingTextEffect : ITreeEffect
    {
        private const float Speed = 0.75f;  // tree units a second (the tree's about 2 wide at the bottom)
        private const float Window = 0.75f; // the text scrolls in from u = +Window and out past u = -Window

        private readonly TreeGeometry _tree;
        private readonly FrontView _view;
        private readonly AnimationColours _colours;
        private readonly Picture _text;

        public ScrollingTextEffect(TreeGeometry tree, string message, TreeOrientation orientation, AnimationColours colours)
        {
            _tree = tree;
            _view = new FrontView(tree, orientation);
            _colours = colours;
            message = string.IsNullOrWhiteSpace(message) ? "MERRY CHRISTMAS" : message.Trim().ToUpperInvariant();
            // The letters sit low on the tree, where it's widest
            _text = TextPicture.Create(message, letterHeight: 1.0f, baseline: tree.ZMax * 0.12f, pixelsPerUnit: 60);
        }

        public void Render(float t, float dt, ColourBuffer frame)
        {
            var palette = _colours.Palette;
            var letters = _colours.Own("Letters");
            var distance = _text.WidthInUnits + 2 * Window + 0.5f; // how far the text travels before it starts again
            var travelled = (t * Speed) % distance;
            _text.Left = Window - travelled;
            for (var i = 0; i < _tree.Count; i++)
            {
                var weight = _view.Weight[i];
                if (weight <= 0)
                {
                    frame.Set(i, 0, 0, 0);
                    continue;
                }
                var u = _view.U[i];
                var ink = _text.Sample(u, _tree.Z[i]).R * weight;
                // The palette's colours along the message, drifting slowly
                var along = (u - _text.Left) / MathF.Max(1, _text.WidthInUnits) + t * 0.03f;
                frame.Set(i, (letters ?? palette.Sample(along)) * ink);
            }
        }
    }

    /// <summary>
    /// Pictures shown in turn on the front of the tree (the side facing the room), cross-fading every few seconds: a star,
    /// a heart, a snowflake and a present, then any images in the Pictures folder (wwwroot/Pictures; dark backgrounds work best).
    /// </summary>
    public sealed class PicturesEffect : ITreeEffect
    {
        private const float PixelsPerUnit = 80;
        private const float SecondsEach = 6;

        private readonly TreeGeometry _tree;
        private readonly FrontView _view;
        private readonly List<Picture> _pictures = [];

        public PicturesEffect(TreeGeometry tree, TreeOrientation orientation, string? picturesFolder)
        {
            _tree = tree;
            _view = new FrontView(tree, orientation);

            // A canvas covering the front of the tree, with the pictures centred where the tree's still quite wide
            var halfWidth = tree.BaseRadius * 1.2f;
            var centreZ = tree.ZMax * 0.36f;
            int width = (int)(2 * halfWidth * PixelsPerUnit), height = (int)(tree.ZMax * PixelsPerUnit);
            var centre = new Point2f(width / 2f, (tree.ZMax - centreZ) * PixelsPerUnit);
            var radius = MathF.Min(0.62f, tree.ConeRadius(centreZ) * 1.4f) * PixelsPerUnit; // how big the pictures are

            foreach (var draw in new Action<Mat, Point2f, float>[] { DrawStar, DrawHeart, DrawSnowflake, DrawPresent })
            {
                using var canvas = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
                draw(canvas, centre, radius);
                Cv2.GaussianBlur(canvas, canvas, new Size(0, 0), PixelsPerUnit * 0.02);
                _pictures.Add(Picture.FromMat(canvas, -halfWidth, tree.ZMax, PixelsPerUnit));
            }

            if (picturesFolder != null && Directory.Exists(picturesFolder))
            {
                var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp" };
                foreach (var file in Directory.GetFiles(picturesFolder).Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).Order())
                {
                    using var image = Cv2.ImRead(file, ImreadModes.Color);
                    if (image.Empty()) continue;
                    // Fit it in a box about as big as the drawn pictures
                    var fit = MathF.Min(2.2f * radius / image.Width, 2.8f * radius / image.Height);
                    using var scaled = new Mat();
                    Cv2.Resize(image, scaled, new Size(Math.Max(1, (int)(image.Width * fit)), Math.Max(1, (int)(image.Height * fit))), 0, 0, InterpolationFlags.Area);
                    var left = -scaled.Width / 2f / PixelsPerUnit;
                    var top = centreZ + scaled.Height / 2f / PixelsPerUnit;
                    _pictures.Add(Picture.FromMat(scaled, left, top, PixelsPerUnit));
                }
            }
        }

        public void Render(float t, float dt, ColourBuffer frame)
        {
            var slot = (int)(t / SecondsEach);
            var current = _pictures[slot % _pictures.Count];
            var next = _pictures[(slot + 1) % _pictures.Count];
            var fade = SmoothStep(SecondsEach - 1, SecondsEach, t % SecondsEach);
            for (var i = 0; i < _tree.Count; i++)
            {
                var weight = _view.Weight[i];
                if (weight <= 0)
                {
                    frame.Set(i, 0, 0, 0);
                    continue;
                }
                float u = _view.U[i], v = _tree.Z[i];
                var colour = fade > 0 ? Rgb.Lerp(current.Sample(u, v), next.Sample(u, v), fade) : current.Sample(u, v);
                var shimmer = 0.85f + 0.3f * (Noise(u * 5, v * 5, t * 0.7f) - 0.5f);
                frame.Set(i, colour * (weight * shimmer));
            }
        }

        private static Scalar Bgr(int r, int g, int b) => new(b, g, r);

        private static Point P(Point2f centre, float dx, float dy) => new((int)Math.Round(centre.X + dx), (int)Math.Round(centre.Y + dy));

        private static void DrawStar(Mat canvas, Point2f centre, float radius)
        {
            var points = Enumerable.Range(0, 10).Select(k =>
            {
                var angle = -MathF.PI / 2 + k * MathF.PI / 5;
                var r = k % 2 == 0 ? radius : radius * 0.42f;
                return P(centre, r * MathF.Cos(angle), r * MathF.Sin(angle));
            }).ToArray();
            Cv2.FillPoly(canvas, new[] { points }, Bgr(255, 190, 30), LineTypes.AntiAlias);
        }

        private static void DrawHeart(Mat canvas, Point2f centre, float radius)
        {
            var scale = radius / 16;
            var points = Enumerable.Range(0, 120).Select(k =>
            {
                var a = k * Tau / 120;
                var x = 16 * MathF.Pow(MathF.Sin(a), 3);
                var y = 13 * MathF.Cos(a) - 5 * MathF.Cos(2 * a) - 2 * MathF.Cos(3 * a) - MathF.Cos(4 * a);
                return P(centre, x * scale, -(y + 2.5f) * scale);
            }).ToArray();
            Cv2.FillPoly(canvas, new[] { points }, Bgr(235, 20, 45), LineTypes.AntiAlias);
        }

        private static void DrawSnowflake(Mat canvas, Point2f centre, float radius)
        {
            var colour = Bgr(190, 225, 255);
            var thickness = Math.Max(2, (int)(radius * 0.16f));
            for (var k = 0; k < 6; k++)
            {
                var angle = -MathF.PI / 2 + k * MathF.PI / 3;
                float dx = MathF.Cos(angle), dy = MathF.Sin(angle);
                Cv2.Line(canvas, P(centre, 0, 0), P(centre, dx * radius, dy * radius), colour, thickness, LineTypes.AntiAlias);
                foreach (var along in new[] { 0.45f, 0.72f })
                {
                    var branch = radius * (along < 0.5f ? 0.34f : 0.24f);
                    foreach (var side in new[] { -1, 1 })
                    {
                        var b = angle + side * MathF.PI / 4;
                        Cv2.Line(canvas, P(centre, dx * radius * along, dy * radius * along),
                                 P(centre, dx * radius * along + MathF.Cos(b) * branch, dy * radius * along + MathF.Sin(b) * branch),
                                 colour, thickness, LineTypes.AntiAlias);
                    }
                }
            }
        }

        private static void DrawPresent(Mat canvas, Point2f centre, float radius)
        {
            var red = Bgr(220, 25, 40);
            var gold = Bgr(255, 200, 60);
            float half = radius * 0.85f, top = -radius * 0.45f, bottom = radius * 0.85f;
            Cv2.Rectangle(canvas, P(centre, -half, top), P(centre, half, bottom), red, -1, LineTypes.AntiAlias);
            var ribbon = radius * 0.13f;
            Cv2.Rectangle(canvas, P(centre, -ribbon, top), P(centre, ribbon, bottom), gold, -1, LineTypes.AntiAlias);
            Cv2.Rectangle(canvas, P(centre, -half, (top + bottom) / 2 - ribbon), P(centre, half, (top + bottom) / 2 + ribbon), gold, -1, LineTypes.AntiAlias);
            // The bow
            var bow = new Size(radius * 0.42f, radius * 0.24f);
            Cv2.Ellipse(canvas, P(centre, -radius * 0.36f, top - radius * 0.2f), bow, -25, 0, 360, gold, -1, LineTypes.AntiAlias);
            Cv2.Ellipse(canvas, P(centre, radius * 0.36f, top - radius * 0.2f), bow, 25, 0, 360, gold, -1, LineTypes.AntiAlias);
            Cv2.Circle(canvas, P(centre, 0, top - radius * 0.06f), (int)(radius * 0.17f), gold, -1, LineTypes.AntiAlias);
        }
    }

    /// <summary>
    /// For setting which side of the tree faces the room: lights that side (dim blue, fading round the sides, the back off)
    /// with a big F in the middle. Turn it until the F faces you, and flip it if the F reads backwards. It follows the
    /// orientation as it's changed.
    /// </summary>
    public sealed class OrientationGuideEffect(TreeGeometry tree, Func<TreeOrientation> orientation) : ITreeEffect
    {
        private readonly Picture _letter = CentredLetter(tree);
        private TreeOrientation? _shown;
        private FrontView? _view;

        private static Picture CentredLetter(TreeGeometry tree)
        {
            var letter = TextPicture.Create("F", letterHeight: 1.3f, baseline: tree.ZMax * 0.1f, pixelsPerUnit: 60);
            letter.Left = -letter.WidthInUnits / 2;
            return letter;
        }

        public void Render(float t, float dt, ColourBuffer frame)
        {
            var current = orientation();
            if (_view == null || current != _shown)
            {
                _view = new FrontView(tree, current);
                _shown = current;
            }
            var background = new Rgb(0.02f, 0.06f, 0.3f);
            for (var i = 0; i < tree.Count; i++)
            {
                var weight = _view.Weight[i];
                var ink = _letter.Sample(_view.U[i], tree.Z[i]).R;
                frame.Set(i, Rgb.Lerp(background, Rgb.White, ink) * weight);
            }
        }
    }
}
