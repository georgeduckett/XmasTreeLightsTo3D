using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Numerics;
using TreeLightsWeb.BackgroundTaskManagement;
using TreeLightsWeb.Extensions;
using TreeLightsWeb.Models;
using TreeLightsWeb.Settings;
using WLEDInterface;

namespace TreeLightsWeb.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IWledClientTaskManager<WledTreeClient> _treeTaskManager;
        private readonly TreePatterns _treePatterns;
        private readonly WledTreeClient _treeClient;
        private readonly NowPlaying _nowPlaying;
        private readonly TreeSettingsService _settings;

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment webHostEnvironment, IWledClientTaskManager<WledTreeClient> treeTaskManager, TreePatterns treePatterns, WledTreeClient treeClient,
                              NowPlaying nowPlaying, TreeSettingsService settings)
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _treeTaskManager = treeTaskManager;
            _treePatterns = treePatterns;
            _treeClient = treeClient;
            _nowPlaying = nowPlaying;
            _settings = settings;
        }

        /// <summary>The animations to choose from, and what's playing (which the page keeps up to date).</summary>
        public IActionResult Index()
        {
            var showcaseCount = TreePatterns.ShowcaseEffects.Count(e => !_settings.ShowcaseExcluded.Contains(e.Name));
            return View(new HomePageModel(TreePatterns.Catalogue, TreePatterns.Catalogue.ToDictionary(a => a.Name, a => _treePatterns.LightColours(a.Name)),
                                          _nowPlaying.State, _settings.Message, _settings.ShowcaseSecondsEach,
                                          showcaseCount == 0 ? TreePatterns.ShowcaseEffects.Count : showcaseCount, _settings.ActivePaletteName));
		}

        public sealed record PlayRequest(string Name, string? Text);

        /// <summary>Plays an animation (for the home page), and returns what's playing once it's started.</summary>
        [HttpPost]
        public async Task<IActionResult> Play([FromBody] PlayRequest request)
        {
            var animation = TreePatterns.FindAnimation(request.Name);
            if (animation == null || _treePatterns.AnimationNamed(animation.Name) == null)
            {
                return BadRequest($"There's no animation called {request.Name} to play.");
            }
            return Json(await PlayAnimation(animation.Name, request.Text));
        }

        /// <summary>Stops whatever's playing (for the home page), and returns what's playing then (nothing).</summary>
        [HttpPost]
        public async Task<IActionResult> Stop()
        {
            return Json(await _nowPlaying.Stop());
        }

        /// <summary>Skips to the next animation in the Showcase.</summary>
        [HttpPost]
        public IActionResult SkipShowcase()
        {
            _treePatterns.RequestShowcaseSkip();
            return Json(_nowPlaying.State);
        }

        /// <summary>What's playing on the tree.</summary>
        [HttpGet]
        public IActionResult CurrentlyPlaying()
        {
            return Json(_nowPlaying.State);
        }

        private Task<NowPlayingState> PlayAnimation(string name, string? text = null)
        {
            var animation = TreePatterns.FindAnimation(name)!;
            return _nowPlaying.Play(animation.Name, animation.Label, _treePatterns.AnimationNamed(animation.Name, text)!);
        }

        private async Task<IActionResult> PlayAndReturn(string name, string? text = null)
        {
            await PlayAnimation(name, text);
            return RedirectToAction("Index");
        }

        public IActionResult Six()
        {
            return PartialView();
        }
		public IActionResult Tree()
		{
			return PartialView();
        }

        public async Task<IActionResult> ReconnectToTree()
        {
            await _treeTaskManager.Reconnect();
            return RedirectToAction("Index");
        }
        public IActionResult IsTreeConnected()
        {
            var isConnected = _treeClient.IsConnected;
            return new ContentResult() { Content = isConnected.ToString(), ContentType = "text/plain", StatusCode = StatusCodes.Status200OK };
        }
        public async Task<IActionResult> StopAnimation()
        {
            await _nowPlaying.Stop();
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> RestoreTreeState()
        {
            await _treeTaskManager.RestoreState();
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> TurnOff()
        {
            await _treeTaskManager.TurnOff();
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> RebootTree()
        {
            await _treeTaskManager.Reboot();
            return RedirectToAction("Index");
        }
        public Task<IActionResult> Contagion() => PlayAndReturn(nameof(Contagion));
        public async Task<IActionResult> StartSyncMusic(string id)
        {
            if(id == "Six")
            {
                await _nowPlaying.Play(nameof(Six), "Six", _treePatterns.Six);
            }
            return StatusCode(StatusCodes.Status200OK);
        }
        public Task<IActionResult> RotateDynamic() => PlayAndReturn(nameof(RotateDynamic));
        public Task<IActionResult> RotateAroundAxis() => PlayAndReturn(nameof(RotateAroundAxis));
        public Task<IActionResult> Snake() => PlayAndReturn(nameof(Snake));
        public Task<IActionResult> Balls() => PlayAndReturn(nameof(Balls));

        public Task<IActionResult> SweepPlanes() => PlayAndReturn(nameof(SweepPlanes));

        public Task<IActionResult> Binary() => PlayAndReturn(nameof(Binary));

        public Task<IActionResult> Fireworks() => PlayAndReturn(nameof(Fireworks));

        public Task<IActionResult> Showcase() => PlayAndReturn(nameof(Showcase));
        public Task<IActionResult> ColourSpiral() => PlayAndReturn(nameof(ColourSpiral));
        public Task<IActionResult> CandyCane() => PlayAndReturn(nameof(CandyCane));
        public Task<IActionResult> Lighthouse() => PlayAndReturn(nameof(Lighthouse));
        public Task<IActionResult> Plasma() => PlayAndReturn(nameof(Plasma));
        public Task<IActionResult> Fire() => PlayAndReturn(nameof(Fire));
        public Task<IActionResult> Ripples() => PlayAndReturn(nameof(Ripples));
        public Task<IActionResult> SpinningCube() => PlayAndReturn(nameof(SpinningCube));
        public Task<IActionResult> Lightning() => PlayAndReturn(nameof(Lightning));
        public Task<IActionResult> Snowfall() => PlayAndReturn(nameof(Snowfall));
        public Task<IActionResult> OrbitingSun() => PlayAndReturn(nameof(OrbitingSun));
        public Task<IActionResult> SloshingLiquid() => PlayAndReturn(nameof(SloshingLiquid));
        public Task<IActionResult> Fairies() => PlayAndReturn(nameof(Fairies));
        public Task<IActionResult> ParticleFireworks() => PlayAndReturn(nameof(ParticleFireworks));
        public Task<IActionResult> LavaLamp() => PlayAndReturn(nameof(LavaLamp));

        /// <summary>Shows the text (or the palette's message) on the front of the tree, as set on the Configuration page.</summary>
        public Task<IActionResult> ScrollingText(string? text) => PlayAndReturn(nameof(ScrollingText), text);

        /// <summary>Shows pictures on the front of the tree, as set on the Configuration page.</summary>
        public Task<IActionResult> Pictures() => PlayAndReturn(nameof(Pictures));

        /// <summary>The page that plays music (or listens to it) and sends what it hears to the music-reactive animation.</summary>
        public IActionResult Music()
        {
            return View();
        }
        public async Task<IActionResult> MusicReactive()
        {
            await _nowPlaying.Play(nameof(MusicReactive), "Music", _treePatterns.MusicReactive);
            return StatusCode(StatusCodes.Status200OK);
        }

        public IActionResult LEDCoordinates()
        {
            if(!System.IO.File.Exists(Path.Combine(_webHostEnvironment.WebRootPath, "Config", "coordinates.csv")))
            {
                return NotFound("Coordinates file not found");
            }

            var coords = System.IO.File.ReadAllText(Path.Combine(_webHostEnvironment.WebRootPath, "Config", "coordinates.csv"));

            var json = Utilities.ConvertCsvFileToJsonObject(coords.Trim());

            return new ContentResult() { Content = json, ContentType = "application/json", StatusCode = StatusCodes.Status200OK };
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
