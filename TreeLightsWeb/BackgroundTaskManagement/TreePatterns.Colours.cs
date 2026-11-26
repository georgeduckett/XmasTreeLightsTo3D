using TreeLightsWeb.BackgroundTaskManagement.Effects;
using WLEDInterface;
using static TreeLightsWeb.BackgroundTaskManagement.Effects.ColourRole;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    // The animations that can be given their own colours on the Configuration page, and what their colours are called.
    // Without their own colours they use their usual ones: from the palette in use for most (Snake's apple is its first
    // colour, say), or, for Fire, Lightning, Snowfall and Orbiting Sun, colours of their own whatever the palette.
    public partial class TreePatterns
    {
        private static Rgb SnakeBody(PaletteSampler palette) => palette.Count > 1 ? palette[1] : palette[0] * 0.5f;

        private static ColourRole[] Halves() => [FromPalette("One half", p => p[0]), FromPalette("Other half", p => p[1])];

        private static readonly ColourScheme RotateAroundAxisColours = new(nameof(RotateAroundAxis), "Rotate Around Axis", Halves());
        private static readonly ColourScheme RotateDynamicColours = new(nameof(RotateDynamic), "Rotate Dynamic", Halves());
        private static readonly ColourScheme BallsColours = new(nameof(Balls), "Balls",
            [FromPalette("First ball", p => p[0]), FromPalette("Second ball", p => p[1]), FromPalette("Third ball", p => p[2])]);
        private static readonly ColourScheme SnakeColours = new(nameof(Snake), "Snake",
            [FromPalette("Head", p => Rgb.Lerp(SnakeBody(p), Rgb.White, 0.6f)), FromPalette("Body", SnakeBody), FromPalette("Apple", p => p[0])]);
        private static readonly ColourScheme FireworksColours = new(nameof(Fireworks), "Fireworks",
            [Fixed("Rocket", "#FFFFFF"), ThroughPalette("Bursts")]);
        private static readonly ColourScheme LighthouseColours = new(nameof(Lighthouse), "Lighthouse", [ThroughPalette("Beam")]);
        private static readonly ColourScheme FireColours = new(nameof(Fire), "Fire",
            [Fixed("Heart of the fire", "#FFFF66"), Fixed("Flames", "#FFE500"), Fixed("Flame tips", "#F51200")]);
        private static readonly ColourScheme SpinningCubeColours = new(nameof(SpinningCube), "Spinning Cube", [ThroughPalette("Cube")]);
        private static readonly ColourScheme LightningColours = new(nameof(Lightning), "Lightning",
            [Fixed("Bolt", "#B3BFFF")]);
        private static readonly ColourScheme SnowfallColours = new(nameof(Snowfall), "Snowfall",
            [Fixed("Snowflakes", "#D9E6FF"), Fixed("Settled snow", "#D9E6FF")]);
        private static readonly ColourScheme OrbitingSunColours = new(nameof(OrbitingSun), "Orbiting Sun",
            [Fixed("Sunlight", "#FFB84D"), Fixed("Night side", "#05081F")]);
        private static readonly ColourScheme SloshingLiquidColours = new(nameof(SloshingLiquid), "Sloshing Liquid",
            [ThroughPalette("Liquid"), Fixed("Foam", "#FFFFFF")]);
        private static readonly ColourScheme FairiesColours = new(nameof(Fairies), "Fairies",
            [FromPalette("First fairy", p => p[0]), FromPalette("Second fairy", p => p[1]), FromPalette("Third fairy", p => p[2])]);
        private static readonly ColourScheme LavaLampColours = new(nameof(LavaLamp), "Lava Lamp",
            [FromPalette("Wax", p => p[0]), FromPalette("Liquid", p => p.Count > 1 ? p[1] : new Rgb(0.55f, 0, 1))]);
        private static readonly ColourScheme ScrollingTextColours = new(nameof(ScrollingText), "Scrolling Text", [ThroughPalette("Letters")]);
        private static readonly ColourScheme MusicColours = new(nameof(MusicReactive), "Music",
            [ThroughPalette("Liquid"), ThroughPalette("Ripples", p => Rgb.Lerp(p[1], Rgb.White, 0.3f)), Fixed("Foam", "#E6F2FF"), Fixed("Sparkles", "#FFFFFF")]);

        /// <summary>The animations that can be given their own colours, in the order the Configuration page lists them.</summary>
        public static IReadOnlyList<ColourScheme> ColourSchemes { get; } =
        [
            RotateAroundAxisColours, RotateDynamicColours, BallsColours, SnakeColours, FireworksColours, LighthouseColours, FireColours,
            SpinningCubeColours, LightningColours, SnowfallColours, OrbitingSunColours, SloshingLiquidColours, FairiesColours,
            LavaLampColours, ScrollingTextColours, MusicColours,
        ];

        public static ColourScheme? FindColourScheme(string? name) =>
            ColourSchemes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The animation with the given name (one with a <see cref="ColourScheme"/>), to show it on the tree.</summary>
        public Func<WledTreeClient, CancellationToken, ValueTask>? AnimationWithColours(string name) => FindColourScheme(name)?.Name switch
        {
            nameof(RotateAroundAxis) => RotateAroundAxis,
            nameof(RotateDynamic) => RotateDynamic,
            nameof(Balls) => Balls,
            nameof(Snake) => Snake,
            nameof(Fireworks) => Fireworks,
            nameof(Lighthouse) => Lighthouse,
            nameof(Fire) => Fire,
            nameof(SpinningCube) => SpinningCube,
            nameof(Lightning) => Lightning,
            nameof(Snowfall) => Snowfall,
            nameof(OrbitingSun) => OrbitingSun,
            nameof(SloshingLiquid) => SloshingLiquid,
            nameof(Fairies) => Fairies,
            nameof(LavaLamp) => LavaLamp,
            nameof(ScrollingText) => (client, cancellationToken) => ScrollingText(client, cancellationToken, null),
            nameof(MusicReactive) => MusicReactive,
            _ => null,
        };
    }
}
