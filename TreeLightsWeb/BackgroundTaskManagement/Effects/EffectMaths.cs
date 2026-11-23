namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    public static class EffectMaths
    {
        public const float Tau = MathF.PI * 2;

        public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, smooth in between.</summary>
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            var t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3 - 2 * t);
        }

        /// <summary>The fractional part (always 0 to 1, also for negative numbers).</summary>
        public static float Fract(float v) => v - MathF.Floor(v);

        /// <summary>An angle wrapped into 0 to 2π.</summary>
        public static float WrapAngle(float a) => (a % Tau + Tau) % Tau;

        private static float Hash(int i, int j, int k)
        {
            unchecked
            {
                var h = i * 374761393 + j * 668265263 + k * 1274126177;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296f;
            }
        }

        /// <summary>Smooth 3D value noise, 0 to 1, varying over distances of about 1.</summary>
        public static float Noise(float x, float y, float z)
        {
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf), w = zf * zf * (3 - 2 * zf);
            static float L(float a, float b, float t) => a + (b - a) * t;
            return L(L(L(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), u), L(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), u), v),
                     L(L(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), u), L(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), u), v), w);
        }

        /// <summary>Noise with some finer detail on top (two octaves).</summary>
        public static float Fbm(float x, float y, float z) => (Noise(x, y, z) * 2 + Noise(2 * x, 2 * y, 2 * z)) / 3;
    }
}
