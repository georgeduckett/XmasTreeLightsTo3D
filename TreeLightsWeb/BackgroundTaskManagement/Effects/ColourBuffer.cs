using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    /// <summary>
    /// One frame of colours, one per LED, as red/green/blue from 0 to 1 (values above 1 are allowed while drawing, e.g. where
    /// glows overlap, and are clipped when the frame is sent).
    /// </summary>
    public sealed class ColourBuffer
    {
        public ColourBuffer(int count)
        {
            R = new float[count];
            G = new float[count];
            B = new float[count];
        }

        public float[] R { get; }
        public float[] G { get; }
        public float[] B { get; }
        public int Count => R.Length;

        public void Clear()
        {
            Array.Clear(R);
            Array.Clear(G);
            Array.Clear(B);
        }

        public void Set(int i, float r, float g, float b)
        {
            R[i] = r; G[i] = g; B[i] = b;
        }

        public void Set(int i, Rgb colour) => Set(i, colour.R, colour.G, colour.B);

        public void Add(int i, float r, float g, float b)
        {
            R[i] += r; G[i] += g; B[i] += b;
        }

        public void Add(int i, Rgb colour, float amount) => Add(i, colour.R * amount, colour.G * amount, colour.B * amount);

        public Rgb this[int i] => new(R[i], G[i], B[i]);

        public RGBValue ToRGBValue(int i) => new(Rgb.ToByte(R[i]), Rgb.ToByte(G[i]), Rgb.ToByte(B[i]));
    }

    /// <summary>A colour as red/green/blue from 0 to 1.</summary>
    public readonly record struct Rgb(float R, float G, float B)
    {
        public static readonly Rgb Black = new(0, 0, 0);
        public static readonly Rgb White = new(1, 1, 1);

        public static Rgb operator *(Rgb c, float k) => new(c.R * k, c.G * k, c.B * k);
        public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);

        public static Rgb Lerp(Rgb a, Rgb b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

        /// <summary>As the 0-255 colour sent to the tree.</summary>
        public RGBValue ToRGBValue() => new(ToByte(R), ToByte(G), ToByte(B));

        /// <summary>As #RRGGBB.</summary>
        public string ToHex() => $"#{ToByte(R):X2}{ToByte(G):X2}{ToByte(B):X2}";

        internal static byte ToByte(float v) => v <= 0 ? (byte)0 : v >= 1 ? (byte)255 : (byte)(v * 255 + 0.5f);

        /// <summary>A colour from hue (0-1, wrapping round: 0 red, 1/3 green, 2/3 blue), saturation and value.</summary>
        public static Rgb FromHsv(float h, float s, float v)
        {
            h = (h % 1 + 1) % 1 * 6;
            var k = (int)h;
            var f = h - k;
            float p = v * (1 - s), q = v * (1 - s * f), u = v * (1 - s * (1 - f));
            return (k % 6) switch
            {
                0 => new(v, u, p),
                1 => new(q, v, p),
                2 => new(p, v, u),
                3 => new(p, q, v),
                4 => new(u, p, v),
                _ => new(v, p, q),
            };
        }
    }
}
