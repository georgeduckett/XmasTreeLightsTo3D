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
        private readonly ITreeTaskManager _treeTaskManager;
        private readonly TreePatterns _treePatterns;
        private readonly WledTreeClient _treeClient;

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment webHostEnvironment, ITreeTaskManager treeTaskManager, TreePatterns treePatterns, WledTreeClient treeClient)
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
            await _treeTaskManager.ReconnectToTree();
            return RedirectToAction("Index");
        }
        public IActionResult IsTreeConnected()
        { // TODO: Add in the javascript in _layout to check if the tree is connected and render the page accordingly
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
            await _treeTaskManager.RestoreTreeState();
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> RebootTree()
        {
            await _treeTaskManager.StopRunningTask();
            await _treeTaskManager.RebootTree();
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Contagion()
        {
            await _treeTaskManager.QueueTreeAnimation(_treePatterns.Contagion);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> StartSyncMusic(string id)
        {
            if(id == "Six")
            {
                await _treeTaskManager.QueueTreeAnimation(_treePatterns.Six);
            }
            return StatusCode(StatusCodes.Status200OK);
        }
        public async Task<IActionResult> RotateDynamic()
        {
            await _treeTaskManager.QueueTreeAnimation(_treePatterns.RotateDynamic);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> RotateAroundAxis()
        {
            await _treeTaskManager.QueueTreeAnimation(_treePatterns.RotateAroundAxis);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Snake()
        {
            await _treeTaskManager.QueueTreeAnimation(_treePatterns.SnakeTest);

            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Balls()
        {
            await _treeTaskManager.QueueTreeAnimation(_treePatterns.Balls);
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> SweepPlanes()
        {
            await _treeTaskManager.QueueTreeAnimation(_treePatterns.SweepPlanes);

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Binary()
        {
            await _treeTaskManager.QueueTreeAnimation(_treePatterns.Binary);

            return RedirectToAction("Index");
        }

        public IActionResult LEDCoordinates()
        {
            var coords = System.IO.File.ReadAllText(Path.Combine(_webHostEnvironment.WebRootPath, "coordinates.csv"));

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
