using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Numerics;
using TreeLightsWeb.BackgroundTaskManagement;
using TreeLightsWeb.Extensions;
using TreeLightsWeb.Models;
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

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment webHostEnvironment, IWledClientTaskManager<WledTreeClient> treeTaskManager, TreePatterns treePatterns, WledTreeClient treeClient)
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _treeTaskManager = treeTaskManager;
            _treePatterns = treePatterns;
            _treeClient = treeClient;
        }

        public IActionResult Index()
        {
            return View();
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
            await _treeTaskManager.StopRunningTask();
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
        public async Task<IActionResult> Contagion()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Contagion);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> StartSyncMusic(string id)
        {
            if(id == "Six")
            {
                await _treeTaskManager.QueueAnimation(_treePatterns.Six);
            }
            return StatusCode(StatusCodes.Status200OK);
        }
        public async Task<IActionResult> RotateDynamic()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.RotateDynamic);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> RotateAroundAxis()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.RotateAroundAxis);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Snake()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Snake);

            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Balls()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Balls);
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> SweepPlanes()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.SweepPlanes);

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Binary()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Binary);

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Fireworks()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Fireworks);

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Showcase()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Showcase);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> ColourSpiral()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.ColourSpiral);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> CandyCane()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.CandyCane);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Lighthouse()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Lighthouse);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Plasma()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Plasma);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Fire()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Fire);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Ripples()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Ripples);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> SpinningCube()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.SpinningCube);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Lightning()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Lightning);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Snowfall()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Snowfall);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> OrbitingSun()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.OrbitingSun);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> SloshingLiquid()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.SloshingLiquid);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Fairies()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Fairies);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> ParticleFireworks()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.ParticleFireworks);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> LavaLamp()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.LavaLamp);
            return RedirectToAction("Index");
        }

        /// <summary>Shows the text (or the palette's message) on the front of the tree, as set on the Configuration page.</summary>
        public async Task<IActionResult> ScrollingText(string? text)
        {
            await _treeTaskManager.QueueAnimation((client, ct) => _treePatterns.ScrollingText(client, ct, text));
            return RedirectToAction("Index");
        }

        /// <summary>Shows pictures on the front of the tree, as set on the Configuration page.</summary>
        public async Task<IActionResult> Pictures()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.Pictures);
            return RedirectToAction("Index");
        }

        /// <summary>The page that plays music (or listens to it) and sends what it hears to the music-reactive animation.</summary>
        public IActionResult Music()
        {
            return View();
        }
        public async Task<IActionResult> MusicReactive()
        {
            await _treeTaskManager.QueueAnimation(_treePatterns.MusicReactive);
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
