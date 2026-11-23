using System.Text.Json;
using TreeLightsWeb.BackgroundTaskManagement.Effects;

namespace TreeLightsWeb.Settings
{
    /// <summary>
    /// Holds the settings from the Configuration page, saves them to wwwroot/Config/TreeSettings.json, and gives the
    /// animations the palette in use and the colours of any animation given its own (which they read every frame, so a
    /// change shows on a running animation).
    /// </summary>
    public sealed class TreeSettingsService : IAnimationColourSource
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        private readonly string _path;
        private readonly ILogger<TreeSettingsService> _logger;
        private readonly object _lock = new();
        private volatile TreeSettings _settings;
        private volatile PaletteSampler _palette;
        // The animations using their own colours: animation name -> colour name -> colour
        private volatile IReadOnlyDictionary<string, Dictionary<string, Rgb>> _ownColours;
        private volatile TreeOrientation _guideOrientation;

        public TreeSettingsService(IWebHostEnvironment environment, ILogger<TreeSettingsService> logger)
        {
            _logger = logger;
            _path = Path.Combine(environment.WebRootPath, "Config", "TreeSettings.json");
            _settings = Load();
            _palette = CreatePalette(_settings);
            _ownColours = CreateOwnColours(_settings);
            _guideOrientation = Orientation;
        }

        /// <summary>A copy of the settings (change them with <see cref="Update"/>).</summary>
        public TreeSettings Current => Copy(_settings);

        /// <summary>The palette in use.</summary>
        public PaletteSampler Palette => _palette;

        /// <summary>An animation's named colours (its own, if it's been given them, otherwise its usual ones).</summary>
        public AnimationColours ColoursFor(ColourScheme scheme) => new(this, scheme);

        public Rgb? OwnColour(string animation, string role) =>
            _ownColours.TryGetValue(animation, out var colours) && colours.TryGetValue(role, out var colour) ? colour : null;

        public TreeOrientation Orientation => new(_settings.FrontDirection, _settings.Mirrored);

        public string ActivePaletteName => _settings.ActivePalette;

        /// <summary>The message for Scrolling Text from the palette in use.</summary>
        public string Message => ActivePaletteSettings(_settings)?.Message ?? "";

        /// <summary>The effects (by name) left out of the Showcase.</summary>
        public IReadOnlyList<string> ShowcaseExcluded => _settings.ShowcaseExcluded;

        public int ShowcaseSecondsEach => _settings.ShowcaseSecondsEach;

        /// <summary>The orientation the guide animation shows while it's being adjusted (it isn't saved until it's right).</summary>
        public TreeOrientation GuideOrientation
        {
            get => _guideOrientation;
            set => _guideOrientation = value;
        }

        /// <summary>Whether the orientation guide animation is on the tree.</summary>
        public bool GuideRunning
        {
            get => _guideRunning;
            set => _guideRunning = value;
        }
        private volatile bool _guideRunning;

        /// <summary>Changes the settings (made sensible, e.g. there's always a palette in use) and saves them.</summary>
        public TreeSettings Update(Action<TreeSettings> change)
        {
            lock (_lock)
            {
                var updated = Copy(_settings);
                change(updated);
                Tidy(updated);
                Save(updated);
                _settings = updated;
                _palette = CreatePalette(updated);
                _ownColours = CreateOwnColours(updated);
                return Copy(updated);
            }
        }

        private TreeSettings Load()
        {
            var settings = new TreeSettings();
            try
            {
                if (File.Exists(_path))
                {
                    settings = JsonSerializer.Deserialize<TreeSettings>(File.ReadAllText(_path), JsonOptions) ?? new TreeSettings();
                }
            }
            catch (Exception ex)
            {
                // Keep the file (it's only replaced when the settings are next saved) and use the defaults for now
                _logger.LogWarning(ex, "Couldn't read the settings in {Path}, so the defaults are being used.", _path);
                settings = new TreeSettings();
            }
            Tidy(settings);
            return settings;
        }

        private void Save(TreeSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }

        private static TreeSettings Copy(TreeSettings settings) =>
            JsonSerializer.Deserialize<TreeSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)!;

        private static PaletteSettings? ActivePaletteSettings(TreeSettings settings) =>
            settings.Palettes.FirstOrDefault(p => string.Equals(p.Name, settings.ActivePalette, StringComparison.OrdinalIgnoreCase));

        private static PaletteSampler CreatePalette(TreeSettings settings)
        {
            var palette = ActivePaletteSettings(settings);
            return palette == null ? PaletteSampler.Rainbow : PaletteSampler.FromHex(palette.Colours, palette.Blend);
        }

        private static Dictionary<string, Dictionary<string, Rgb>> CreateOwnColours(TreeSettings settings)
        {
            var own = new Dictionary<string, Dictionary<string, Rgb>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (animation, colours) in settings.AnimationColours.Where(a => a.Value.OwnColours))
            {
                var parsed = new Dictionary<string, Rgb>();
                foreach (var (role, hex) in colours.Colours)
                {
                    if (PaletteSampler.TryParseHex(hex, out var colour)) parsed[role] = colour;
                }
                own[animation] = parsed;
            }
            return own;
        }

        private static string NormaliseHex(string hex) => "#" + hex.Trim().TrimStart('#').ToUpperInvariant();

        /// <summary>Makes the settings sensible: valid colours, unique palette names, a palette in use, and so on.</summary>
        private static void Tidy(TreeSettings settings)
        {
            settings.FrontDirection = ((settings.FrontDirection % 360) + 360) % 360;
            settings.ShowcaseSecondsEach = Math.Clamp(settings.ShowcaseSecondsEach, 5, 600);
            settings.ShowcaseExcluded = (settings.ShowcaseExcluded ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();

            settings.Palettes = (settings.Palettes ?? []).Where(p => p != null).ToList();
            if (settings.Palettes.Count == 0)
            {
                settings.Palettes = TreeSettings.DefaultPalettes();
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var palette in settings.Palettes)
            {
                palette.Colours = (palette.Colours ?? []).Where(c => PaletteSampler.TryParseHex(c, out _)).Select(NormaliseHex).ToList();
                if (palette.Colours.Count == 0) palette.Colours = ["#FFFFFF"];
                palette.Blend = Math.Clamp(palette.Blend, 0, 1);
                palette.Message = (palette.Message ?? "").Trim();
                var name = string.IsNullOrWhiteSpace(palette.Name) ? "Palette" : palette.Name.Trim();
                var unique = name;
                for (var n = 2; !names.Add(unique); n++) unique = $"{name} {n}";
                palette.Name = unique;
            }
            settings.ActivePalette = (ActivePaletteSettings(settings) ?? settings.Palettes[0]).Name;

            // Animations' own colours: valid colours only (and one entry per animation, whatever the case of its name)
            var animationColours = new Dictionary<string, AnimationColourSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var (animation, entry) in settings.AnimationColours ?? [])
            {
                if (string.IsNullOrWhiteSpace(animation) || entry == null || animationColours.ContainsKey(animation.Trim())) continue;
                var colours = new Dictionary<string, string>();
                foreach (var (role, hex) in entry.Colours ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(role) && PaletteSampler.TryParseHex(hex, out _)) colours[role.Trim()] = NormaliseHex(hex);
                }
                animationColours[animation.Trim()] = new AnimationColourSettings { OwnColours = entry.OwnColours, Colours = colours };
            }
            settings.AnimationColours = animationColours;
        }
    }
}
