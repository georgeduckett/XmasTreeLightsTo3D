using System.Globalization;
using static TreeLightsWeb.BackgroundTaskManagement.Effects.EffectMaths;

namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    /// <summary>Where effects get the colour palette in use from (it can change while they run).</summary>
    public interface IPaletteSource
    {
        PaletteSampler Palette { get; }
    }

    /// <summary>
    /// A palette's colours, in order, for effects to use: some take colours one at a time (the first two for a two colour
    /// effect, a random one for each firework, ...) and some sample it as a loop that goes through all the colours.
    /// </summary>
    public sealed class PaletteSampler : IPaletteSource
    {
        private readonly Rgb[] _colours;
        private readonly float _blend;

        /// <param name="blend">0: each colour is a solid band when sampled; 1: a smooth gradient from one to the next.</param>
        public PaletteSampler(IReadOnlyList<Rgb> colours, float blend)
        {
            _colours = colours.Count == 0 ? [Rgb.White] : colours.ToArray();
            _blend = Clamp01(blend);
        }

        /// <summary>From colours written as #RRGGBB (any that aren't are left out).</summary>
        public static PaletteSampler FromHex(IEnumerable<string> colours, double blend) =>
            new(colours.Select(c => TryParseHex(c, out var rgb) ? rgb : (Rgb?)null).Where(c => c != null).Select(c => c!.Value).ToArray(), (float)blend);

        public static bool TryParseHex(string? text, out Rgb colour)
        {
            colour = Rgb.Black;
            if (text == null) return false;
            var hex = text.Trim().TrimStart('#');
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;
            colour = new Rgb(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f);
            return true;
        }

        /// <summary>All the colours of the rainbow (what the colourful effects use if there's no palette).</summary>
        public static PaletteSampler Rainbow { get; } = FromHex(["#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF"], 1);

        PaletteSampler IPaletteSource.Palette => this;

        public int Count => _colours.Length;

        /// <summary>The palette's colours in turn (wrapping round, so any index works).</summary>
        public Rgb this[int index] => _colours[((index % _colours.Length) + _colours.Length) % _colours.Length];

        public Rgb Random(Random random) => _colours[random.Next(_colours.Length)];

        /// <summary>
        /// The colour <paramref name="position"/> of the way round the palette (0 to 1, wrapping round): the colours in turn,
        /// as solid bands blending into each other as much as the palette's blend says.
        /// </summary>
        public Rgb Sample(float position)
        {
            var x = Fract(position) * _colours.Length;
            var index = (int)x;
            var into = x - index;
            if (_blend <= 0.001f) return this[index];
            return Rgb.Lerp(this[index], this[index + 1], SmoothStep(1 - _blend, 1, into));
        }

        /// <summary>
        /// Like <see cref="Sample"/> but always with crisp bands (a short blend between colours, whatever the palette's own
        /// blend), for stripes.
        /// </summary>
        public Rgb Band(float position, float edge = 0.15f)
        {
            var x = Fract(position) * _colours.Length;
            var index = (int)x;
            return Rgb.Lerp(this[index], this[index + 1], SmoothStep(1 - edge, 1, x - index));
        }
    }

    /// <summary>
    /// Which side of the tree faces the room (degrees round the trunk from the side the camera saw in the first captured
    /// image, anticlockwise seen from above), and whether the LED positions are a mirror image of the real tree.
    /// </summary>
    public sealed record TreeOrientation(int FrontDirection, bool Mirrored);
}
