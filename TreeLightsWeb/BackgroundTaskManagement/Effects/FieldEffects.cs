using static TreeLightsWeb.BackgroundTaskManagement.Effects.EffectMaths;

namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    // Effects where each LED's colour is a function of where it is and the time.

    /// <summary>
    /// The palette's colours wound round the tree, drifting upwards: colour = palette at θ/2π + 1.6·height − 0.2t (with the
    /// Rainbow palette, a rainbow).
    /// </summary>
    public sealed class ColourSpiralEffect(TreeGeometry tree, IPaletteSource palette) : ITreeEffect
    {
        public void Render(float t, float dt, ColourBuffer frame)
        {
            var colours = palette.Palette;
            for (var i = 0; i < tree.Count; i++)
            {
                frame.Set(i, colours.Sample(tree.Theta[i] / Tau + tree.Height[i] * 1.6f - t * 0.2f));
            }
        }
    }

    /// <summary>
    /// Stripes spiralling up the tree in the palette's colours, one colour after another (a candy cane with a red and
    /// white palette): stripe number = (2θ + 14·height − 3t) / π.
    /// </summary>
    public sealed class CandyCaneEffect(TreeGeometry tree, IPaletteSource palette) : ITreeEffect
    {
        public void Render(float t, float dt, ColourBuffer frame)
        {
            var colours = palette.Palette;
            for (var i = 0; i < tree.Count; i++)
            {
                var stripe = (2 * tree.Theta[i] + tree.Height[i] * 14 - t * 3) / MathF.PI;
                frame.Set(i, colours.Band(stripe / colours.Count));
            }
        }
    }

    /// <summary>
    /// A beam sweeping round the trunk with a fading wake: brightness = e^(−2.2 × angle behind the beam). Its colour
    /// drifts slowly through the palette (or is its own "Beam" colour).
    /// </summary>
    public sealed class LighthouseEffect(TreeGeometry tree, AnimationColours colours) : ITreeEffect
    {
        public void Render(float t, float dt, ColourBuffer frame)
        {
            var beam = t * 1.6f;
            var colour = colours.Through("Beam", t * 0.02f);
            for (var i = 0; i < tree.Count; i++)
            {
                var behind = WrapAngle(beam - tree.Theta[i]);
                frame.Set(i, colour * (MathF.Exp(-behind * 2.2f) + 0.03f));
            }
        }
    }

    /// <summary>
    /// A slowly morphing field of colour: a sum of sine waves through x, y, z and the distance from the middle picks the
    /// colour from the palette.
    /// </summary>
    public sealed class PlasmaEffect(TreeGeometry tree, IPaletteSource palette) : ITreeEffect
    {
        private readonly float _centreZ = tree.ZMax * 0.5f;

        public void Render(float t, float dt, ColourBuffer frame)
        {
            var colours = palette.Palette;
            for (var i = 0; i < tree.Count; i++)
            {
                float x = tree.X[i], y = tree.Y[i], z = tree.Z[i], dz = z - _centreZ;
                var v = MathF.Sin(x * 2.5f + t) + MathF.Sin(y * 2.5f - 1.3f * t) + MathF.Sin(z * 2 + 0.7f * t)
                      + MathF.Sin(MathF.Sqrt(x * x + y * y + dz * dz) * 3.5f - 1.6f * t);
                var s = MathF.Sin(v * 1.5f);
                frame.Set(i, colours.Sample(v / 8 + t * 0.05f) * (0.55f + 0.45f * s * s));
            }
        }
    }

    /// <summary>
    /// Flames licking up the tree: heat = (1 − height) + rising 3D noise, coloured black → flame tips → flames → the heart
    /// of the fire as it gets hotter (red, yellow and pale yellow usually).
    /// </summary>
    public sealed class FireEffect(TreeGeometry tree, AnimationColours colours) : ITreeEffect
    {
        // How hot (0-1) each colour is: black at 0, then the flame tips, the flames, and the heart at 1
        private const float TipsHeat = 0.42f, FlamesHeat = 0.9f;

        public void Render(float t, float dt, ColourBuffer frame)
        {
            Rgb heart = colours["Heart of the fire"], flames = colours["Flames"], tips = colours["Flame tips"];
            for (var i = 0; i < tree.Count; i++)
            {
                var heat = 1.15f - tree.Height[i] * 1.35f + (Fbm(tree.X[i] * 2.2f, tree.Y[i] * 2.2f, tree.Z[i] * 1.7f - t * 1.9f) - 0.5f) * 1.3f;
                var k = Clamp01(heat);
                frame.Set(i, k < TipsHeat ? tips * (k / TipsHeat)
                           : k < FlamesHeat ? Rgb.Lerp(tips, flames, (k - TipsHeat) / (FlamesHeat - TipsHeat))
                           : Rgb.Lerp(flames, heart, (k - FlamesHeat) / (1 - FlamesHeat)));
            }
        }
    }

    /// <summary>
    /// A wireframe cube tumbling inside the tree: LEDs near its edges light up, in a colour drifting through the palette
    /// (or its own "Cube" colour).
    /// </summary>
    public sealed class SpinningCubeEffect : ITreeEffect
    {
        private readonly TreeGeometry _tree;
        private readonly AnimationColours _colours;
        private readonly float _centreZ, _halfSize;

        public SpinningCubeEffect(TreeGeometry tree, AnimationColours colours)
        {
            _tree = tree;
            _colours = colours;
            _centreZ = tree.ZMax * 0.45f;
            // As big as it can be while still being mostly inside the tree (with ~900 LEDs, edges need to be thick to read)
            _halfSize = tree.ConeRadius(_centreZ) * 1.1f;
        }

        public void Render(float t, float dt, ColourBuffer frame)
        {
            float a = t * 0.7f, b = t * 0.45f;
            float ca = MathF.Cos(a), sa = MathF.Sin(a), cb = MathF.Cos(b), sb = MathF.Sin(b), s = _halfSize;
            var colour = _colours.Through("Cube", t * 0.03f);
            Span<float> p = stackalloc float[3];
            for (var i = 0; i < _tree.Count; i++)
            {
                // The LED's position in the cube's (rotating) frame
                float x = _tree.X[i], y = _tree.Y[i], z = _tree.Z[i] - _centreZ;
                float x1 = ca * x + sa * y, y1 = -sa * x + ca * y;
                float y2 = cb * y1 + sb * z, z2 = -sb * y1 + cb * z;
                p[0] = MathF.Abs(x1); p[1] = MathF.Abs(y2); p[2] = MathF.Abs(z2);
                // Distance to the nearest of the cube's 12 edges
                var d = float.MaxValue;
                for (var k = 0; k < 3; k++)
                {
                    float u = p[(k + 1) % 3] - s, v = p[(k + 2) % 3] - s, o = MathF.Max(0, p[k] - s);
                    d = MathF.Min(d, MathF.Sqrt(u * u + v * v + o * o));
                }
                frame.Set(i, colour * MathF.Exp(-(d * d) / 0.018f));
            }
        }
    }

    /// <summary>The tree lit like a solid object by a sun circling it, with a day side and a night side.</summary>
    public sealed class OrbitingSunEffect(TreeGeometry tree, AnimationColours colours) : ITreeEffect
    {
        public void Render(float t, float dt, ColourBuffer frame)
        {
            Rgb sunlight = colours["Sunlight"], night = colours["Night side"];
            var angle = t * 0.6f;
            float lx = MathF.Cos(angle), ly = MathF.Sin(angle), lz = 0.35f, ll = MathF.Sqrt(lx * lx + ly * ly + lz * lz);
            for (var i = 0; i < tree.Count; i++)
            {
                // Roughly the normal of the cone's surface at the LED (pointing out, and a bit up)
                float nx = tree.X[i], ny = tree.Y[i], nz = 0.45f * tree.R[i];
                var nl = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl == 0) nl = 1;
                var lit = MathF.Max(0, (nx * lx + ny * ly + nz * lz) / (nl * ll));
                var e = MathF.Pow(lit, 1.3f);
                frame.Set(i, night + sunlight * e);
            }
        }
    }

    /// <summary>
    /// The tree filling up with liquid (in a colour drifting through the palette, or its own "Liquid" colour) and
    /// draining, with the surface tilting and wobbling, and foam on top.
    /// </summary>
    public sealed class SloshingLiquidEffect(TreeGeometry tree, AnimationColours colours) : ITreeEffect
    {
        public void Render(float t, float dt, ColourBuffer frame)
        {
            var liquid = colours.Through("Liquid", t * 0.015f);
            var foamColour = colours["Foam"];
            var cycle = (t % 14) / 14;
            var level = (cycle < 0.6f ? cycle / 0.6f : 1 - (cycle - 0.6f) / 0.4f) * tree.ZMax * 1.05f;
            float tilt = 0.22f * MathF.Sin(t * 1.9f), tiltDirection = t * 0.5f, cx = MathF.Cos(tiltDirection), cy = MathF.Sin(tiltDirection);
            for (var i = 0; i < tree.Count; i++)
            {
                var surface = level + tilt * (tree.X[i] * cx + tree.Y[i] * cy);
                var depth = surface - tree.Z[i];
                if (depth < -0.08f)
                {
                    frame.Set(i, 0, 0, 0);
                    continue;
                }
                var foam = MathF.Exp(-(depth * depth) / 0.004f);
                var shade = depth > 0 ? 0.45f + 0.55f * Noise(tree.X[i] * 3, tree.Y[i] * 3, tree.Z[i] * 3 + t * 1.2f) : 0;
                frame.Set(i, MathF.Min(1, liquid.R * 0.85f * shade + foamColour.R * foam), MathF.Min(1, liquid.G * 0.85f * shade + foamColour.G * foam),
                          MathF.Min(1, liquid.B * 0.85f * shade + foamColour.B * foam));
            }
        }
    }
}
