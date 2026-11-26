using TreeLightsWeb.BackgroundTaskManagement.Effects;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    /// <summary>
    /// An animation as the home page lists it: <see cref="Name"/> is its name in TreePatterns (and its Home action), and
    /// <see cref="Page"/> is set for one that's started from its own page (e.g. Music) rather than played straight away.
    /// </summary>
    public sealed record AnimationInfo(string Name, string Label, string Description, string Group, string? Page = null);

    // What the home page lists, and how it plays each one
    public partial class TreePatterns
    {
        public const string EffectsGroup = "3D effects", WordsGroup = "Words and pictures", MusicGroup = "Music",
                            ClassicsGroup = "Classics", TestsGroup = "Tests";

        /// <summary>Every animation, in the home page's order (its groups in the order they first appear).</summary>
        public static IReadOnlyList<AnimationInfo> Catalogue { get; } =
        [
            new(nameof(Showcase), "Showcase", "The 3D effects one after another, with a wipe from each to the next.", EffectsGroup),
            new(nameof(ColourSpiral), "Colour Spiral", "Bands of colour spiralling up the tree.", EffectsGroup),
            new(nameof(CandyCane), "Candy Cane", "Stripes twisting round the tree like a candy cane.", EffectsGroup),
            new(nameof(Lighthouse), "Lighthouse", "A beam of light sweeping round the trunk.", EffectsGroup),
            new(nameof(Plasma), "Plasma", "Soft colours swirling slowly through the tree.", EffectsGroup),
            new(nameof(Fire), "Fire", "Flames licking up from the base.", EffectsGroup),
            new(nameof(Ripples), "Ripples", "Rings of colour spreading out through the tree.", EffectsGroup),
            new(nameof(SpinningCube), "Spinning Cube", "A wireframe cube tumbling inside the tree.", EffectsGroup),
            new(nameof(Lightning), "Lightning", "Bolts forking down the tree, lighting it up as they strike.", EffectsGroup),
            new(nameof(Snowfall), "Snowfall", "Snow drifting down and settling at the bottom.", EffectsGroup),
            new(nameof(OrbitingSun), "Orbiting Sun", "A sun circling the tree, with a day side and a night side.", EffectsGroup),
            new(nameof(SloshingLiquid), "Sloshing Liquid", "The tree filling with liquid that sloshes about and drains.", EffectsGroup),
            new(nameof(Fairies), "Fairies", "Three fairies spiralling up, trailing light.", EffectsGroup),
            new(nameof(ParticleFireworks), "Particle Fireworks", "Rockets bursting into showers of falling sparks.", EffectsGroup),
            new(nameof(LavaLamp), "Lava Lamp", "Blobs of glowing wax rising and sinking.", EffectsGroup),
            new(nameof(ScrollingText), "Scrolling Text", "A message scrolling across the front of the tree.", WordsGroup),
            new(nameof(Pictures), "Pictures", "A star, a heart, a snowflake and a present on the front, then any of your own.", WordsGroup),
            new(nameof(MusicReactive), "Music", "Lights that move to music played or heard on the music page.", MusicGroup, "Music"),
            new(nameof(Six), "Six", "A light show timed to the video on its page.", MusicGroup, "Six"),
            new(nameof(RotateAroundAxis), "Rotate Around Axis", "The tree split into two colours by a turning plane.", ClassicsGroup),
            new(nameof(RotateDynamic), "Rotate Dynamic", "Two colours split by a plane that tilts as it turns.", ClassicsGroup),
            new(nameof(Balls), "Balls", "Three coloured balls bouncing around inside the tree.", ClassicsGroup),
            new(nameof(Snake), "Snake", "The game of Snake in 3D, playing itself.", ClassicsGroup),
            new(nameof(Contagion), "Contagion", "Colour spreading out from light to light.", ClassicsGroup),
            new(nameof(Fireworks), "Fireworks", "Rockets climbing up the tree and bursting.", ClassicsGroup),
            new(nameof(SweepPlanes), "Sweep Planes", "A white plane sweeping up, across and through the tree.", TestsGroup),
            new(nameof(Binary), "Flash Binary", "Every light flashing its number in binary.", TestsGroup),
        ];

        public static AnimationInfo? FindAnimation(string? name) =>
            Catalogue.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The animation to play for a <see cref="Catalogue"/> entry (null for one with its own page, which starts it).
        /// <paramref name="text"/> is the message for Scrolling Text (null for the palette's).
        /// </summary>
        public Func<WledTreeClient, CancellationToken, ValueTask>? AnimationNamed(string name, string? text = null) => FindAnimation(name)?.Name switch
        {
            nameof(Showcase) => Showcase,
            nameof(ColourSpiral) => ColourSpiral,
            nameof(CandyCane) => CandyCane,
            nameof(Lighthouse) => Lighthouse,
            nameof(Plasma) => Plasma,
            nameof(Fire) => Fire,
            nameof(Ripples) => Ripples,
            nameof(SpinningCube) => SpinningCube,
            nameof(Lightning) => Lightning,
            nameof(Snowfall) => Snowfall,
            nameof(OrbitingSun) => OrbitingSun,
            nameof(SloshingLiquid) => SloshingLiquid,
            nameof(Fairies) => Fairies,
            nameof(ParticleFireworks) => ParticleFireworks,
            nameof(LavaLamp) => LavaLamp,
            nameof(ScrollingText) => (client, cancellationToken) => ScrollingText(client, cancellationToken, text),
            nameof(Pictures) => Pictures,
            nameof(RotateAroundAxis) => RotateAroundAxis,
            nameof(RotateDynamic) => RotateDynamic,
            nameof(Balls) => Balls,
            nameof(Snake) => Snake,
            nameof(Contagion) => Contagion,
            nameof(Fireworks) => Fireworks,
            nameof(SweepPlanes) => SweepPlanes,
            nameof(Binary) => Binary,
            _ => null,
        };

        /// <summary>
        /// Seven colours (#RRGGBB) the animation shows, with the settings as they are, for the home page to draw it as a
        /// string of lights: its own colours (or usual ones) for one that has named colours, otherwise the palette's.
        /// </summary>
        public IReadOnlyList<string> LightColours(string name)
        {
            const int Lights = 7;
            var palette = _settings.Palette;
            IReadOnlyList<Rgb> colours = name switch
            {
                nameof(SweepPlanes) => [Rgb.White],
                nameof(Binary) => [Rgb.White, new Rgb(1, 0, 0)],
                nameof(Six) => [new Rgb(1, 0, 0), new Rgb(0, 1, 0), new Rgb(0, 0, 1)],
                nameof(Pictures) => [new Rgb(1, 0.82f, 0.2f), new Rgb(0.9f, 0.15f, 0.2f), new Rgb(0.65f, 0.85f, 1), new Rgb(0.2f, 0.7f, 0.3f)],
                _ => ColourSchemes.FirstOrDefault(s => s.Name == name) is { } scheme ? SchemeColours(scheme, palette) : [],
            };
            if (colours.Count == 0)
            {
                // Along the palette, as the colourful animations show it
                return Enumerable.Range(0, Lights).Select(i => palette.Sample(i / (float)Lights).ToHex()).ToList();
            }
            return Enumerable.Range(0, Lights).Select(i => colours[i % colours.Count].ToHex()).ToList();
        }

        private List<Rgb> SchemeColours(ColourScheme scheme, PaletteSampler palette)
        {
            var colours = _settings.ColoursFor(scheme);
            var lights = new List<Rgb>();
            foreach (var role in scheme.Roles)
            {
                if (role.Kind == ColourRoleKind.ThroughPalette && colours.Own(role.Name) == null)
                {
                    // (It goes through the palette's colours)
                    lights.AddRange(Enumerable.Range(0, palette.Count).Select(i => palette[i]));
                }
                else
                {
                    lights.Add(colours[role.Name]);
                }
            }
            return lights;
        }
    }
}
