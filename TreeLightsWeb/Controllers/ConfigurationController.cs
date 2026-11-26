using Microsoft.AspNetCore.Mvc;
using TreeLightsWeb.BackgroundTaskManagement;
using TreeLightsWeb.BackgroundTaskManagement.Effects;
using TreeLightsWeb.Settings;
using WLEDInterface;

namespace TreeLightsWeb.Controllers
{
    /// <summary>
    /// The Configuration page: which side of the tree faces the room, how dim a lit LED can be, the colour palettes (and
    /// which is in use), the colours of animations given their own, each animation's speed, and which animations the
    /// Showcase goes through. The page's script calls the actions below, which save the settings and return them.
    /// </summary>
    public class ConfigurationController(TreeSettingsService settings, TreePatterns treePatterns, WledTreeClient treeClient, NowPlaying nowPlaying)
        : Controller
    {
        public sealed record OrientationRequest(int FrontDirection, bool Mirrored);
        public sealed record DimmestLevelRequest(int DimmestLevel, int SwitchOffBelowPercent);
        public sealed record ShowcaseRequest(List<string> Included, int SecondsEach);
        public sealed record NameRequest(string Name);
        /// <summary>A palette to save, replacing the one called <see cref="OriginalName"/> (or added, if that's empty).</summary>
        public sealed record PaletteRequest(string? OriginalName, PaletteSettings Palette);
        /// <summary>Whether an animation uses its own colours, and (any of) its colours by name, as #RRGGBB.</summary>
        public sealed record AnimationColoursRequest(string Name, bool OwnColours, Dictionary<string, string>? Colours);
        /// <summary>An animation's speed multiplier (1 is normal speed).</summary>
        public sealed record AnimationSpeedRequest(string Name, double Speed);

        public IActionResult Index()
        {
            return View();
        }

        /// <summary>The settings, the animations whose speed or Showcase inclusion can be set, and how the LEDs' colours are sent to WLED.</summary>
        [HttpGet]
        public IActionResult Settings()
        {
            return Json(new
            {
                settings = settings.Current,
                showcaseEffects = TreePatterns.ShowcaseEffects.Select(e => new { e.Name, e.Label }),
                speedAnimations = TreePatterns.SpeedAnimations.Select(a => new { a.Name, a.Label, a.Group }),
                speedLimits = new { min = TreeSettingsService.MinSpeed, max = TreeSettingsService.MaxSpeed },
                leds = LedsInfo(),
            });
        }

        /// <summary>Sets how dim a lit LED can be (it shows on the tree straight away).</summary>
        [HttpPost]
        public IActionResult DimmestLevel([FromBody] DimmestLevelRequest request)
        {
            return Json(settings.Update(s =>
            {
                s.DimmestLevel = request.DimmestLevel;
                s.SwitchOffBelowPercent = request.SwitchOffBelowPercent;
            }));
        }

        /// <summary>
        /// Puts the test pattern for choosing the dimmest level on the tree, and says how much WLED's dimming the LEDs
        /// (read again, in case its brightness has changed).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DimLevelsTest()
        {
            await treeClient.RefreshBrightnessAsync();
            await nowPlaying.Play(nameof(TreePatterns.DimLevelsTest), "Dim LED test pattern", treePatterns.DimLevelsTest);
            return Json(LedsInfo());
        }

        private object LedsInfo() => new { usesDdp = treeClient.UsesDdp, wledBrightness = treeClient.RealtimeBrightness };

        /// <summary>Puts the orientation guide on the tree showing the given orientation (or moves it, if it's on already). Doesn't save anything.</summary>
        [HttpPost]
        public async Task<IActionResult> Guide([FromBody] OrientationRequest request)
        {
            settings.GuideOrientation = new TreeOrientation(request.FrontDirection, request.Mirrored);
            if (!settings.GuideRunning)
            {
                await nowPlaying.Play(nameof(TreePatterns.OrientationGuide), "Orientation guide", treePatterns.OrientationGuide);
            }
            return Ok();
        }

        [HttpPost]
        public IActionResult Orientation([FromBody] OrientationRequest request)
        {
            return Json(settings.Update(s =>
            {
                s.FrontDirection = request.FrontDirection;
                s.Mirrored = request.Mirrored;
            }));
        }

        [HttpPost]
        public IActionResult Showcase([FromBody] ShowcaseRequest request)
        {
            if (request.Included == null || request.Included.Count == 0)
            {
                return BadRequest("Tick at least one animation for the Showcase.");
            }
            return Json(settings.Update(s =>
            {
                s.ShowcaseExcluded = TreePatterns.ShowcaseEffects.Select(e => e.Name).Where(name => !request.Included.Contains(name)).ToList();
                s.ShowcaseSecondsEach = request.SecondsEach;
            }));
        }

        /// <summary>Switches to another palette (a running animation changes colour straight away).</summary>
        [HttpPost]
        public IActionResult UsePalette([FromBody] NameRequest request)
        {
            if (!settings.Current.Palettes.Any(p => SameName(p.Name, request.Name)))
            {
                return BadRequest($"There's no palette called {request.Name}.");
            }
            return Json(settings.Update(s => s.ActivePalette = request.Name));
        }

        [HttpPost]
        public IActionResult SavePalette([FromBody] PaletteRequest request)
        {
            var palette = request.Palette;
            if (palette == null || string.IsNullOrWhiteSpace(palette.Name))
            {
                return BadRequest("The palette needs a name.");
            }
            if (palette.Colours == null || !palette.Colours.Any(c => PaletteSampler.TryParseHex(c, out _)))
            {
                return BadRequest("The palette needs at least one colour.");
            }
            var current = settings.Current;
            var index = string.IsNullOrWhiteSpace(request.OriginalName) ? -1 : current.Palettes.FindIndex(p => SameName(p.Name, request.OriginalName));
            if (current.Palettes.Where((p, i) => i != index).Any(p => SameName(p.Name, palette.Name)))
            {
                return BadRequest($"There's already a palette called {palette.Name.Trim()}.");
            }
            return Json(settings.Update(s =>
            {
                palette.Name = palette.Name.Trim();
                // (Found again here, in case another save has changed the palettes since the check above)
                var position = index < 0 ? -1 : s.Palettes.FindIndex(p => SameName(p.Name, request.OriginalName));
                if (position >= 0)
                {
                    if (SameName(s.ActivePalette, s.Palettes[position].Name)) s.ActivePalette = palette.Name; // it's still the one in use
                    s.Palettes[position] = palette;
                }
                else
                {
                    s.Palettes.Add(palette);
                }
            }));
        }

        [HttpPost]
        public IActionResult DeletePalette([FromBody] NameRequest request)
        {
            var current = settings.Current;
            if (current.Palettes.Count <= 1)
            {
                return BadRequest("There has to be at least one palette.");
            }
            // (If it's the one in use, the first palette is used instead)
            return Json(settings.Update(s => s.Palettes.RemoveAll(p => SameName(p.Name, request.Name))));
        }

        /// <summary>
        /// The animations that can be given their own colours: what each of their colours is called, what it usually is (from
        /// the palette in use, for most) and what it's been set to, and whether the animation's using its own colours.
        /// </summary>
        [HttpGet]
        public IActionResult AnimationColours()
        {
            return Json(AnimationColourList(settings.Current));
        }

        /// <summary>
        /// Switches an animation's own colours on or off, and sets any of them (a running animation changes straight away).
        /// When they're first switched on, any not given start as the animation's usual colours.
        /// </summary>
        [HttpPost]
        public IActionResult SaveAnimationColours([FromBody] AnimationColoursRequest request)
        {
            var scheme = TreePatterns.FindColourScheme(request.Name);
            if (scheme == null)
            {
                return BadRequest($"There's no animation called {request.Name} that can have its own colours.");
            }
            var palette = settings.Palette;
            var updated = settings.Update(s =>
            {
                var entry = new AnimationColourSettings { OwnColours = request.OwnColours, Colours = new(SavedColours(s, scheme)?.Colours ?? []) };
                foreach (var role in scheme.Roles)
                {
                    if (request.Colours != null && request.Colours.TryGetValue(role.Name, out var hex) && PaletteSampler.TryParseHex(hex, out _))
                    {
                        entry.Colours[role.Name] = hex;
                    }
                    else if (!entry.Colours.ContainsKey(role.Name))
                    {
                        entry.Colours[role.Name] = role.Usual(palette).ToHex();
                    }
                }
                foreach (var name in s.AnimationColours.Keys.Where(k => SameName(k, scheme.Name)).ToList())
                {
                    s.AnimationColours.Remove(name);
                }
                s.AnimationColours[scheme.Name] = entry;
            });
            return Json(AnimationColourList(updated));
        }

        /// <summary>Sets an animation's speed multiplier (a running animation changes speed straight away).</summary>
        [HttpPost]
        public IActionResult SaveAnimationSpeed([FromBody] AnimationSpeedRequest request)
        {
            var info = TreePatterns.SpeedAnimations.FirstOrDefault(a => SameName(a.Name, request.Name));
            if (info == null)
            {
                return BadRequest($"There's no animation called {request.Name} whose speed can be set.");
            }
            if (!double.IsFinite(request.Speed))
            {
                return BadRequest("The speed must be a number.");
            }
            return Json(settings.Update(s =>
            {
                foreach (var key in s.AnimationSpeeds.Keys.Where(k => SameName(k, info.Name)).ToList())
                {
                    s.AnimationSpeeds.Remove(key);
                }
                s.AnimationSpeeds[info.Name] = request.Speed;
            }));
        }

        /// <summary>Resets every animation back to normal (1x) speed.</summary>
        [HttpPost]
        public IActionResult ResetAnimationSpeeds()
        {
            return Json(settings.Update(s => s.AnimationSpeeds.Clear()));
        }

        /// <summary>Shows an animation on the tree, to see its colours or speed while they're chosen.</summary>
        [HttpPost]
        public async Task<IActionResult> ShowAnimation([FromBody] NameRequest request)
        {
            var info = TreePatterns.FindAnimation(request.Name);
            var animation = info == null ? null : treePatterns.AnimationForPreview(info.Name);
            if (info == null || animation == null)
            {
                return BadRequest($"There's no animation called {request.Name}.");
            }
            await nowPlaying.Play(info.Name, info.Label, animation);
            return Ok();
        }

        private object AnimationColourList(TreeSettings current)
        {
            var palette = settings.Palette;
            return TreePatterns.ColourSchemes.Select(scheme =>
            {
                var saved = SavedColours(current, scheme);
                return new
                {
                    scheme.Name,
                    scheme.Label,
                    OwnColours = saved?.OwnColours ?? false,
                    Roles = scheme.Roles.Select(role => new
                    {
                        role.Name,
                        Kind = role.Kind.ToString(),
                        Usual = role.Usual(palette).ToHex(),
                        Own = saved?.Colours.GetValueOrDefault(role.Name),
                    }),
                };
            });
        }

        private static AnimationColourSettings? SavedColours(TreeSettings s, ColourScheme scheme) =>
            s.AnimationColours.FirstOrDefault(a => SameName(a.Key, scheme.Name)).Value;

        private static bool SameName(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
