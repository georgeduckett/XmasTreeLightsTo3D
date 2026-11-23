using static TreeLightsWeb.BackgroundTaskManagement.Effects.EffectMaths;

namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    /// <summary>
    /// A lava lamp: blobs of glowing wax (the palette's first colour, or its own "Wax" colour) slowly rising and sinking
    /// through dark liquid (the palette's second, or its own "Liquid" colour) inside the cone, merging and splitting as
    /// they pass (metaballs: an LED is inside the wax where the sum of size²/distance² over the blobs is over 1).
    /// </summary>
    public sealed class LavaLampEffect : ITreeEffect
    {
        private sealed record Blob(float Angle, float Spin, float Out, float Period, float Phase, float Size);

        private readonly TreeGeometry _tree;
        private readonly AnimationColours _colours;
        private readonly Blob[] _blobs;
        private readonly float[] _bx, _by, _bz, _bs;

        public LavaLampEffect(TreeGeometry tree, AnimationColours colours)
        {
            _tree = tree;
            _colours = colours;
            var random = new Random();
            _blobs = Enumerable.Range(0, 6).Select(_ => new Blob(
                Angle: random.NextSingle() * Tau,
                Spin: (0.05f + random.NextSingle() * 0.1f) * (random.Next(2) == 0 ? -1 : 1),
                Out: 0.25f + random.NextSingle() * 0.5f,
                Period: 16 + random.NextSingle() * 12,
                Phase: random.NextSingle(),
                Size: 0.2f + random.NextSingle() * 0.1f)).ToArray();
            _bx = new float[_blobs.Length]; _by = new float[_blobs.Length]; _bz = new float[_blobs.Length]; _bs = new float[_blobs.Length];
        }

        public void Render(float t, float dt, ColourBuffer frame)
        {
            var tree = _tree;
            for (var b = 0; b < _blobs.Length; b++)
            {
                var blob = _blobs[b];
                // Up and down at a steady speed, slowing a little to turn round (so they don't all pile up at the ends)
                var upDown = 1 - MathF.Abs(2 * Fract(t / blob.Period + blob.Phase) - 1);
                var z = tree.ZMax * (0.15f + 0.75f * (0.5f * upDown + 0.5f * SmoothStep(0, 1, upDown)));
                var cone = tree.ConeRadius(z);
                var angle = blob.Angle + blob.Spin * t;
                _bx[b] = blob.Out * cone * MathF.Cos(angle);
                _by[b] = blob.Out * cone * MathF.Sin(angle);
                _bz[b] = z;
                // Smaller near the narrow top
                var size = blob.Size * (0.55f + 0.45f * cone / tree.BaseRadius);
                _bs[b] = size * size;
            }

            var waxLow = _colours["Wax"];
            var waxHigh = Rgb.Lerp(waxLow, Rgb.White, 0.25f); // a little paler where it's risen (and cooled)
            var liquidColour = _colours["Liquid"];
            for (var i = 0; i < tree.Count; i++)
            {
                var field = 0f;
                for (var b = 0; b < _blobs.Length; b++)
                {
                    field += _bs[b] / (tree.DistanceSquared(i, _bx[b], _by[b], _bz[b]) + 0.002f);
                }
                var wax = SmoothStep(0.9f, 1.3f, field);
                var halo = SmoothStep(0.4f, 0.9f, field) * 0.2f * (1 - wax);
                var waxColour = Rgb.Lerp(waxLow, waxHigh, tree.Height[i]);
                var liquid = liquidColour * (0.18f * (0.6f + 0.4f * Noise(tree.X[i] * 2, tree.Y[i] * 2, tree.Z[i] * 2 - t * 0.15f)));
                var lamp = waxLow * (0.25f * MathF.Exp(-tree.Z[i] / 0.2f)); // the bulb's heat at the bottom
                frame.Set(i, liquid * (1 - wax) + waxColour * (wax + halo) + lamp);
            }
        }
    }
}
