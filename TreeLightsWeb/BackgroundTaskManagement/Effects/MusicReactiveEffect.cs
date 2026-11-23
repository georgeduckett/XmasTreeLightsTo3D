using static TreeLightsWeb.BackgroundTaskManagement.Effects.EffectMaths;

namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    /// <summary>
    /// Reacts to the music the music page is playing or hearing: the bass sets how full of glowing liquid the tree is, each
    /// beat sends a ripple up from the base, and the treble makes LEDs sparkle. The colours come from the palette, moving
    /// on a colour every beat (unless it's been given its own colours). With no music it idles, gently breathing.
    /// </summary>
    public sealed class MusicReactiveEffect(TreeGeometry tree, MusicLevels music, AnimationColours animationColours) : ITreeEffect
    {
        private readonly List<(float Start, Rgb Colour)> _ripples = [];
        private readonly float[] _sparkle = new float[tree.Count];
        private readonly Random _random = new();
        private float _level = 0.1f;
        private int _beat;
        private Rgb? _liquid;
        private int? _lastBeats;

        public void Render(float t, float dt, ColourBuffer frame)
        {
            var colours = animationColours.Palette;
            Rgb? ownLiquid = animationColours.Own("Liquid"), ownRipples = animationColours.Own("Ripples");
            Rgb foamColour = animationColours["Foam"], sparkleColour = animationColours["Sparkles"];
            var heard = music.Read();
            float bass = 0, mid = 0, treble = 0;
            float target;
            if (heard.IsLive)
            {
                (bass, mid, treble) = (heard.Bass, heard.Mid, heard.Treble);
                target = 0.08f + 0.85f * bass;
                if (_lastBeats != null && heard.Beats != _lastBeats)
                {
                    var newBeats = Math.Clamp(heard.Beats - _lastBeats.Value, 1, 2);
                    for (var b = 0; b < newBeats; b++)
                    {
                        _beat++;
                        _ripples.Add((t + b * 0.08f, ownRipples ?? Rgb.Lerp(colours[_beat + 1], Rgb.White, 0.3f)));
                    }
                }
                _lastBeats = heard.Beats;
            }
            else
            {
                target = 0.12f + 0.05f * MathF.Sin(t * 1.2f); // waiting for music
                _lastBeats = null;
            }
            // Rise quickly with the bass, fall back more slowly
            _level += (target - _level) * (1 - MathF.Exp(-dt * (target > _level ? 12 : 3)));
            _ripples.RemoveAll(r => t - r.Start > 1.2f);

            // The liquid's colour follows the beat's colour slowly, over a few seconds
            var beatColour = ownLiquid ?? colours[_beat];
            var liquid = Rgb.Lerp(_liquid ?? beatColour, beatColour, 1 - MathF.Exp(-dt / 4));
            _liquid = liquid;

            var surface = _level * tree.ZMax;
            var sparkleChance = treble * treble * 0.03f * dt * 40;
            var sparkleFade = MathF.Exp(-dt * 9);
            for (var i = 0; i < tree.Count; i++)
            {
                float x = tree.X[i], y = tree.Y[i], z = tree.Z[i];
                var depth = surface - z;
                var colour = Rgb.Black;
                if (depth > 0)
                {
                    var swirl = Noise(x * 2.5f, y * 2.5f, z * 2.5f - t * 0.6f);
                    colour = liquid * (0.22f + 0.45f * mid + 0.15f * swirl);
                }
                var foam = MathF.Exp(-(depth * depth) / 0.006f) * (0.5f + 0.5f * bass);
                colour += foamColour * foam;

                // Ripples: spheres growing out of the middle of the base
                var fromBase = MathF.Sqrt(x * x + y * y + z * z);
                foreach (var (start, rippleColour) in _ripples)
                {
                    var age = t - start;
                    if (age < 0) continue;
                    var fromShell = fromBase - age * 2.4f;
                    var a = MathF.Exp(-(fromShell * fromShell) / 0.01f) * (1 - age / 1.2f);
                    if (a > 0.01f) colour += rippleColour * a;
                }

                _sparkle[i] *= sparkleFade;
                if (_random.NextSingle() < sparkleChance) _sparkle[i] = 1;
                colour += sparkleColour * _sparkle[i];

                frame.Set(i, colour);
            }
        }
    }
}
