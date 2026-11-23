namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    /// <summary>What one of an animation's named colours is while the animation isn't using its own colours.</summary>
    public enum ColourRoleKind
    {
        /// <summary>One of the palette's colours (or a colour made from them).</summary>
        Palette,

        /// <summary>It goes through the palette's colours as the animation runs.</summary>
        ThroughPalette,

        /// <summary>The animation's usual colour, whatever the palette.</summary>
        Fixed,
    }

    /// <summary>
    /// One of the colours an animation can be given (the snake's head, say), and what it is otherwise: worked out from the
    /// palette in use (for one that goes through the palette, the colour it starts from), or the animation's usual colour.
    /// </summary>
    public sealed record ColourRole(string Name, ColourRoleKind Kind, Func<PaletteSampler, Rgb> Usual)
    {
        public static ColourRole FromPalette(string name, Func<PaletteSampler, Rgb> colour) => new(name, ColourRoleKind.Palette, colour);

        public static ColourRole ThroughPalette(string name, Func<PaletteSampler, Rgb>? start = null) =>
            new(name, ColourRoleKind.ThroughPalette, start ?? (palette => palette[0]));

        public static ColourRole Fixed(string name, string hex) =>
            PaletteSampler.TryParseHex(hex, out var colour) ? new(name, ColourRoleKind.Fixed, _ => colour)
                                                            : throw new ArgumentException($"{hex} isn't a colour", nameof(hex));
    }

    /// <summary>The colours an animation can be given, by name (<see cref="Name"/> is the animation's name in TreePatterns).</summary>
    public sealed record ColourScheme(string Name, string Label, IReadOnlyList<ColourRole> Roles)
    {
        public ColourRole Role(string name) =>
            Roles.FirstOrDefault(r => r.Name == name) ?? throw new ArgumentException($"{Label} has no colour called {name}", nameof(name));
    }

    /// <summary>Where animations get their colours: the palette in use, and the colours of any animation given its own.</summary>
    public interface IAnimationColourSource : IPaletteSource
    {
        /// <summary>The colour the animation's been given for the role, if it's using its own colours (otherwise null).</summary>
        Rgb? OwnColour(string animation, string role);
    }

    /// <summary>
    /// An animation's named colours: its own, if it's been given its own colours on the Configuration page, otherwise its
    /// usual ones (from the palette in use, for most). They're looked up each time, so a change shows straight away.
    /// </summary>
    public sealed class AnimationColours(IAnimationColourSource source, ColourScheme scheme)
    {
        public ColourScheme Scheme => scheme;

        /// <summary>The palette in use.</summary>
        public PaletteSampler Palette => source.Palette;

        public Rgb this[string role]
        {
            get
            {
                var colourRole = scheme.Role(role);
                return source.OwnColour(scheme.Name, role) ?? colourRole.Usual(source.Palette);
            }
        }

        /// <summary>The animation's own colour for the role, if it's using its own colours (otherwise null).</summary>
        public Rgb? Own(string role)
        {
            _ = scheme.Role(role); // (so a misspelt name fails straight away)
            return source.OwnColour(scheme.Name, role);
        }

        /// <summary>
        /// For a colour that goes through the palette: the palette at <paramref name="position"/> (see
        /// <see cref="PaletteSampler.Sample"/>), or the animation's own colour if it's using its own colours.
        /// </summary>
        public Rgb Through(string role, float position) => Own(role) ?? source.Palette.Sample(position);
    }
}
