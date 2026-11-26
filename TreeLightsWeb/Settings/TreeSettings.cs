namespace TreeLightsWeb.Settings
{
    /// <summary>The settings on the Configuration page (saved in wwwroot/Config/TreeSettings.json).</summary>
    public sealed class TreeSettings
    {
        /// <summary>
        /// Which side of the tree faces the room, in degrees round the trunk: 0 is the side that faced the camera in the
        /// first captured image, and it goes anticlockwise seen from above. Text and pictures are shown on this side.
        /// </summary>
        public int FrontDirection { get; set; }

        /// <summary>Whether the LED positions are a mirror image of the real tree (so text would otherwise read backwards).</summary>
        public bool Mirrored { get; set; }

        /// <summary>
        /// The dimmest level (0-255, as the LEDs get it) a lit LED is shown at, or 0 to show every level as it is: very dim
        /// LEDs can't show colours properly (they tend to look red), so ones that would be dimmer are switched off (below
        /// <see cref="SwitchOffBelowPercent"/>) or brightened up to it, keeping their colour.
        /// </summary>
        public int DimmestLevel { get; set; } = 8;

        /// <summary>Where LEDs are switched off rather than brightened up to <see cref="DimmestLevel"/>, as a percentage of it.</summary>
        public int SwitchOffBelowPercent { get; set; } = 25;

        /// <summary>
        /// The effects (by name) left out of the Showcase. It's the ones left out rather than the ones in, so any effect added
        /// later is in the Showcase unless it's taken out.
        /// </summary>
        public List<string> ShowcaseExcluded { get; set; } = [];

        /// <summary>How long the Showcase shows each effect for.</summary>
        public int ShowcaseSecondsEach { get; set; } = 30;

        /// <summary>The name of the palette in use.</summary>
        public string ActivePalette { get; set; } = "Christmas";

        public List<PaletteSettings> Palettes { get; set; } = DefaultPalettes();

        /// <summary>
        /// The colours chosen for particular animations (by animation name, e.g. "Snake"): ones with their own colours
        /// use these, whatever palette's in use, rather than their usual colours.
        /// </summary>
        public Dictionary<string, AnimationColourSettings> AnimationColours { get; set; } = [];

        /// <summary>
        /// Speed multipliers for particular animations (by animation name, e.g. "Snake"): 1 is normal speed, and anything
        /// not listed runs at 1.
        /// </summary>
        public Dictionary<string, double> AnimationSpeeds { get; set; } = [];

        /// <summary>A palette for each season (switching between them is up to you), plus a rainbow.</summary>
        public static List<PaletteSettings> DefaultPalettes() =>
        [
            new() { Name = "Christmas", Colours = ["#FF0000", "#00C020", "#FFB000", "#FFE6B0"], Blend = 0.3, Message = "Merry Christmas" },
            new() { Name = "Winter", Colours = ["#60C8FF", "#FFFFFF", "#2040FF", "#B090FF"], Blend = 0.6, Message = "Let It Snow" },
            new() { Name = "Spring", Colours = ["#FFD020", "#C080FF", "#50FF9A", "#FF7AC0"], Blend = 0.5, Message = "Happy Easter" },
            new() { Name = "Summer", Colours = ["#FF7A00", "#00E0C8", "#FF2090", "#FFE000"], Blend = 0.5, Message = "Hello Summer" },
            new() { Name = "Autumn", Colours = ["#FF6A00", "#D02010", "#FFB020", "#C07818"], Blend = 0.5, Message = "Happy Autumn" },
            new() { Name = "Halloween", Colours = ["#FF5A00", "#8A00FF", "#50FF00"], Blend = 0.3, Message = "Happy Halloween" },
            new() { Name = "Rainbow", Colours = ["#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF"], Blend = 1, Message = "Hello" },
        ];
    }

    public sealed class PaletteSettings
    {
        public string Name { get; set; } = "";

        /// <summary>
        /// The colours, as #RRGGBB, in the order effects use them: two colour effects (like Rotate Around Axis) use the first
        /// two, Snake's apple is the first and its body the second, and the colourful effects go through them all (unless
        /// an animation's been given its own colours, see <see cref="TreeSettings.AnimationColours"/>).
        /// </summary>
        public List<string> Colours { get; set; } = [];

        /// <summary>How effects that go through the colours blend them: 0 for solid bands of each colour, 1 for a smooth gradient.</summary>
        public double Blend { get; set; } = 0.5;

        /// <summary>The message Scrolling Text shows (unless it's given one) while this palette's in use.</summary>
        public string Message { get; set; } = "";
    }

    public sealed class AnimationColourSettings
    {
        /// <summary>
        /// Whether the animation uses these colours. If not, it uses its usual ones (from the palette in use, for most),
        /// and these are kept for when it's switched back.
        /// </summary>
        public bool OwnColours { get; set; }

        /// <summary>Its colours by name (e.g. "Head" for Snake), as #RRGGBB.</summary>
        public Dictionary<string, string> Colours { get; set; } = [];
    }
}
