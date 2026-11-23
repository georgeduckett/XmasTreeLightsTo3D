using static TreeLightsWeb.BackgroundTaskManagement.Effects.EffectMaths;

namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    // Effects that simulate things moving through the tree.

    /// <summary>Shells (each a random colour from the palette) expanding from random LEDs, adding together where they cross.</summary>
    public sealed class RipplesEffect(TreeGeometry tree, IPaletteSource palette) : ITreeEffect
    {
        private readonly record struct Drop(int Led, float Start, Rgb Colour);

        private readonly List<Drop> _drops = [];
        private readonly Random _random = new();
        private float _nextDrop;

        public void Render(float t, float dt, ColourBuffer frame)
        {
            if (t >= _nextDrop && tree.Count > 0)
            {
                _drops.Add(new Drop(_random.Next(tree.Count), t, palette.Palette.Random(_random)));
                _nextDrop = t + 0.45f + _random.NextSingle() * 0.4f;
                if (_drops.Count > 7) _drops.RemoveAt(0);
            }

            frame.Clear();
            foreach (var drop in _drops)
            {
                var age = t - drop.Start;
                var fade = 1 - age / 3.2f;
                if (fade <= 0) continue;
                var radius = age * 0.9f;
                float cx = tree.X[drop.Led], cy = tree.Y[drop.Led], cz = tree.Z[drop.Led];
                for (var i = 0; i < tree.Count; i++)
                {
                    var fromShell = MathF.Sqrt(tree.DistanceSquared(i, cx, cy, cz)) - radius;
                    var a = MathF.Exp(-(fromShell * fromShell) / 0.012f) * fade;
                    if (a > 0.01f) frame.Add(i, drop.Colour, a);
                }
            }
        }
    }

    /// <summary>
    /// Lightning: a branching walk down from the top of the tree to the bottom, LED to LED along the nearest-neighbour
    /// graph, then a flash, a flicker and an afterglow.
    /// </summary>
    public sealed class LightningEffect : ITreeEffect
    {
        private readonly TreeGeometry _tree;
        private readonly AnimationColours _colours;
        private readonly float[] _lit;
        private readonly Random _random = new();
        private float _strikeTime = -9, _nextStrike = 0.3f;
        private int _steps;

        public LightningEffect(TreeGeometry tree, AnimationColours colours)
        {
            _tree = tree;
            _colours = colours;
            _lit = new float[tree.Count];
            _ = tree.Neighbours; // work them out now rather than in the middle of the first strike
        }

        public void Render(float t, float dt, ColourBuffer frame)
        {
            if (t >= _nextStrike && _tree.Count > 0)
            {
                Array.Clear(_lit);
                _strikeTime = t;
                _nextStrike = t + 1.6f + _random.NextSingle() * 1.8f;
                _steps = 0;
                var top = 0;
                for (var i = 0; i < _tree.Count; i++)
                {
                    if (_tree.Z[i] > _tree.ZMax * 0.8f && _random.NextSingle() < 0.15f && _tree.Z[i] > _tree.Z[top] * 0.9f) top = i;
                }
                Walk(top, 200);
            }

            var age = t - _strikeTime;
            var flash = age < 0.07f ? 1 : age < 0.14f ? 0.25f : age < 0.2f ? 0.9f : MathF.Exp(-(age - 0.2f) * 3.5f);
            var sky = 0.18f * (age < 0.2f ? flash : 0); // the whole tree lit faintly by the flash
            Rgb bolt = _colours["Bolt"], skyFlash = _colours["Sky flash"] * sky;
            for (var i = 0; i < _tree.Count; i++)
            {
                frame.Set(i, bolt * (_lit[i] * flash) + skyFlash);
            }
        }

        private void Walk(int led, int steps)
        {
            var neighbours = _tree.Neighbours;
            for (var n = 0; n < steps && _steps < 2000; n++, _steps++)
            {
                _lit[led] = 1;
                var z = _tree.Z[led];
                var down = neighbours[led].Where(j => _tree.Z[j] < z - 0.01f).ToArray();
                if (down.Length == 0)
                {
                    // No lower neighbour nearby: jump to the nearest lower LED (if there's one within 0.5)
                    int best = -1;
                    var bestDistance = 0.25f;
                    for (var j = 0; j < _tree.Count; j++)
                    {
                        if (_tree.Z[j] >= z - 0.01f) continue;
                        var d = _tree.DistanceSquared(j, _tree.X[led], _tree.Y[led], z);
                        if (d < bestDistance) { bestDistance = d; best = j; }
                    }
                    if (best < 0) break;
                    down = [best];
                }
                led = down[_random.Next(Math.Min(3, down.Length))];
                if (_random.NextSingle() < 0.1f) Walk(led, 6 + _random.Next(10)); // a branch
            }
        }
    }

    /// <summary>Snowflakes drifting down through the tree, settling into a layer of snow that builds up from the bottom.</summary>
    public sealed class SnowfallEffect(TreeGeometry tree, AnimationColours colours) : ITreeEffect
    {
        private sealed class Flake
        {
            public float X, Y, Z, Speed, Phase;
        }

        private readonly List<Flake> _flakes = [];
        private readonly Random _random = new();
        private float _snowLevel;

        public void Render(float t, float dt, ColourBuffer frame)
        {
            var spawnRadius = tree.BaseRadius * 1.3f;
            while (_flakes.Count < 34)
            {
                var a = _random.NextSingle() * Tau;
                var r = MathF.Sqrt(_random.NextSingle()) * spawnRadius;
                _flakes.Add(new Flake
                {
                    X = r * MathF.Cos(a), Y = r * MathF.Sin(a), Z = tree.ZMax + _random.NextSingle() * 1.2f,
                    Speed = 0.3f + _random.NextSingle() * 0.25f, Phase = _random.NextSingle() * Tau,
                });
            }
            for (var f = _flakes.Count - 1; f >= 0; f--)
            {
                var flake = _flakes[f];
                flake.Z -= flake.Speed * dt;
                flake.X += MathF.Sin(t * 1.3f + flake.Phase) * 0.12f * dt;
                if (flake.Z < _snowLevel)
                {
                    _flakes.RemoveAt(f);
                    _snowLevel += 0.004f;
                }
            }
            if (_snowLevel > tree.ZMax * 0.27f) _snowLevel = 0;

            Rgb flakeColour = colours["Snowflakes"], snowColour = colours["Settled snow"];
            for (var i = 0; i < tree.Count; i++)
            {
                var snow = tree.Z[i] < _snowLevel ? 0.35f + 0.1f * MathF.Sin(t * 2 + i) : 0;
                var flakes = 0f;
                foreach (var flake in _flakes)
                {
                    var d2 = tree.DistanceSquared(i, flake.X, flake.Y, flake.Z);
                    if (d2 < 0.09f) flakes += MathF.Exp(-d2 / 0.018f);
                }
                // As bright as the two together (up to full brightness), in a mix of their colours
                var a = snow + flakes;
                frame.Set(i, a <= 0 ? Rgb.Black : Rgb.Lerp(snowColour, flakeColour, flakes / a) * MathF.Min(1, a));
            }
        }
    }

    /// <summary>Three comets (in the palette's first three colours) spiralling up the tree, each with a fading tail.</summary>
    public sealed class FairiesEffect(TreeGeometry tree, AnimationColours colours) : ITreeEffect
    {
        private static readonly string[] Fairies = ["First fairy", "Second fairy", "Third fairy"];
        private const int TailLength = 22;

        public void Render(float t, float dt, ColourBuffer frame)
        {
            frame.Clear();
            for (var k = 0; k < Fairies.Length; k++)
            {
                var colour = colours[Fairies[k]];
                for (var j = 0; j < TailLength; j++)
                {
                    // How far up the tree (0-1) this point of the comet (j = 0 is its head) is
                    var along = Fract(t * 0.05f + k / 3f - j * 0.0035f);
                    var z = along * tree.ZMax;
                    var angle = along * Tau * 6 + k * Tau / 3;
                    var r = tree.ConeRadius(z);
                    var weight = MathF.Pow(1 - j / (float)TailLength, 1.5f);
                    float px = r * MathF.Cos(angle), py = r * MathF.Sin(angle);
                    for (var i = 0; i < tree.Count; i++)
                    {
                        var d2 = tree.DistanceSquared(i, px, py, z);
                        if (d2 < 0.06f) frame.Add(i, colour, MathF.Exp(-d2 / 0.01f) * weight);
                    }
                }
            }
        }
    }
}
