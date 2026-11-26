using TreeLightsWeb.BackgroundTaskManagement.Effects;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    // Animations made from the effects in the Effects folder (each draws a frame from the LED positions and the time)
    public partial class TreePatterns
    {
        /// <summary>The effects the Showcase can include (name and label), in the order the Configuration page lists them.</summary>
        public static IReadOnlyList<(string Name, string Label)> ShowcaseEffects { get; } =
        [
            (nameof(ColourSpiral), "Colour Spiral"), (nameof(CandyCane), "Candy Cane"), (nameof(Lighthouse), "Lighthouse"),
            (nameof(Plasma), "Plasma"), (nameof(Fire), "Fire"), (nameof(Ripples), "Ripples"), (nameof(SpinningCube), "Spinning Cube"),
            (nameof(Lightning), "Lightning"), (nameof(Snowfall), "Snowfall"), (nameof(OrbitingSun), "Orbiting Sun"),
            (nameof(SloshingLiquid), "Sloshing Liquid"), (nameof(Fairies), "Fairies"), (nameof(ParticleFireworks), "Particle Fireworks"),
            (nameof(LavaLamp), "Lava Lamp"), (nameof(ScrollingText), "Scrolling Text"), (nameof(Pictures), "Pictures"),
        ];

        /// <summary>Makes the effect with the given name (from <see cref="ShowcaseEffects"/>), using the current settings.</summary>
        private ITreeEffect CreateEffect(string name, TreeGeometry tree) => name switch
        {
            nameof(ColourSpiral) => new ColourSpiralEffect(tree, _settings),
            nameof(CandyCane) => new CandyCaneEffect(tree, _settings),
            nameof(Lighthouse) => new LighthouseEffect(tree, _settings.ColoursFor(LighthouseColours)),
            nameof(Plasma) => new PlasmaEffect(tree, _settings),
            nameof(Fire) => new FireEffect(tree, _settings.ColoursFor(FireColours)),
            nameof(Ripples) => new RipplesEffect(tree, _settings),
            nameof(SpinningCube) => new SpinningCubeEffect(tree, _settings.ColoursFor(SpinningCubeColours)),
            nameof(Lightning) => new LightningEffect(tree, _settings.ColoursFor(LightningColours)),
            nameof(Snowfall) => new SnowfallEffect(tree, _settings.ColoursFor(SnowfallColours)),
            nameof(OrbitingSun) => new OrbitingSunEffect(tree, _settings.ColoursFor(OrbitingSunColours)),
            nameof(SloshingLiquid) => new SloshingLiquidEffect(tree, _settings.ColoursFor(SloshingLiquidColours)),
            nameof(Fairies) => new FairiesEffect(tree, _settings.ColoursFor(FairiesColours)),
            nameof(ParticleFireworks) => new ParticleFireworksEffect(tree, _settings),
            nameof(LavaLamp) => new LavaLampEffect(tree, _settings.ColoursFor(LavaLampColours)),
            nameof(ScrollingText) => new ScrollingTextEffect(tree, _settings.Message, _settings.Orientation, _settings.ColoursFor(ScrollingTextColours)),
            nameof(Pictures) => new PicturesEffect(tree, _settings.Orientation, Path.Combine(_environment.WebRootPath, "Pictures")),
            _ => throw new ArgumentException($"There's no effect called {name}", nameof(name)),
        };

        private ValueTask RunEffect(WledTreeClient client, CancellationToken cancellationToken, string name) =>
            RunEffect(client, cancellationToken, tree => CreateEffect(name, tree), name);

        public ValueTask ColourSpiral(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(ColourSpiral));
        public ValueTask CandyCane(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(CandyCane));
        public ValueTask Lighthouse(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(Lighthouse));
        public ValueTask Plasma(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(Plasma));
        public ValueTask Fire(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(Fire));
        public ValueTask Ripples(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(Ripples));
        public ValueTask SpinningCube(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(SpinningCube));
        public ValueTask Lightning(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(Lightning));
        public ValueTask Snowfall(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(Snowfall));
        public ValueTask OrbitingSun(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(OrbitingSun));
        public ValueTask SloshingLiquid(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(SloshingLiquid));
        public ValueTask Fairies(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(Fairies));
        public ValueTask ParticleFireworks(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(ParticleFireworks));
        public ValueTask LavaLamp(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(LavaLamp));
        public ValueTask Pictures(WledTreeClient client, CancellationToken cancellationToken) => RunEffect(client, cancellationToken, nameof(Pictures));

        /// <summary>A message (or, if there isn't one, the palette's) scrolling across the front of the tree.</summary>
        public ValueTask ScrollingText(WledTreeClient client, CancellationToken cancellationToken, string? text) =>
            RunEffect(client, cancellationToken, tree => new ScrollingTextEffect(tree, string.IsNullOrWhiteSpace(text) ? _settings.Message : text,
                                                                                 _settings.Orientation, _settings.ColoursFor(ScrollingTextColours)),
                      nameof(ScrollingText));

        /// <summary>
        /// The effects ticked on the Configuration page in turn, with a wipe from one to the next (which effect it's on, and
        /// which is next, are shown on the home page).
        /// </summary>
        public ValueTask Showcase(WledTreeClient client, CancellationToken cancellationToken) =>
            RunEffect(client, cancellationToken, tree => new ShowcaseEffect(tree, ShowcaseLineUp, name => CreateEffect(name, tree),
                () => _settings.ShowcaseSecondsEach, _settings.SpeedFor,
                (name, next, seconds) =>
                {
                    Console.WriteLine($"Showcase: {ShowcaseLabel(name)}");
                    _nowPlaying.ShowcaseShowing(name, ShowcaseLabel(name), seconds, next, ShowcaseLabel(next));
                }));

        private static string ShowcaseLabel(string name) => ShowcaseEffects.FirstOrDefault(e => e.Name == name).Label ?? name;

        /// <summary>The effects ticked for the Showcase (all of them if none are).</summary>
        private IReadOnlyList<string> ShowcaseLineUp()
        {
            var excluded = _settings.ShowcaseExcluded;
            var included = ShowcaseEffects.Select(e => e.Name).Where(name => !excluded.Contains(name)).ToList();
            return included.Count > 0 ? included : ShowcaseEffects.Select(e => e.Name).ToList();
        }

        /// <summary>Reacts to the music the Music page is playing or hearing.</summary>
        public ValueTask MusicReactive(WledTreeClient client, CancellationToken cancellationToken) =>
            RunEffect(client, cancellationToken, tree => new MusicReactiveEffect(tree, _musicLevels, _settings.ColoursFor(MusicColours)),
                      nameof(MusicReactive));

        /// <summary>
        /// For the Configuration page: shows which side is set as the front, following the slider as it's moved, so it can
        /// be turned to face the room.
        /// </summary>
        public async ValueTask OrientationGuide(WledTreeClient client, CancellationToken cancellationToken)
        {
            _settings.GuideRunning = true;
            try
            {
                await RunEffect(client, cancellationToken, tree => new OrientationGuideEffect(tree, () => _settings.GuideOrientation));
            }
            finally
            {
                _settings.GuideRunning = false;
            }
        }

        /// <summary>
        /// The test pattern for choosing the dimmest LED level on the Configuration page (see <see cref="DimLevelsTestEffect"/>).
        /// </summary>
        public ValueTask DimLevelsTest(WledTreeClient client, CancellationToken cancellationToken) =>
            RunEffect(client, cancellationToken, tree => new DimLevelsTestEffect(tree, _settings.Orientation, (float)client.RealtimeGamma));
    }
}
