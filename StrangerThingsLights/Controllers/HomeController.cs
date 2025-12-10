using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using StrangerThingsLights.BackgroundTaskManagement;
using StrangerThingsLights.Models;
using System.Diagnostics;
using System.Numerics;
using WLEDInterface;

namespace StrangerThingsLights.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IWledClientTaskManager<WledClient> _wledTaskManager;
        private readonly WledPatterns _wledPatterns;
        private readonly WledClient _wledClient;

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment webHostEnvironment, IWledClientTaskManager<WledClient> wledTaskManager, WledPatterns wledPatterns, WledClient wledClient)
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _wledTaskManager = wledTaskManager;
            _wledPatterns = wledPatterns;
            _wledClient = wledClient;
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
            await _wledTaskManager.Reconnect();
            return RedirectToAction("Index");
        }
        public IActionResult IsTreeConnected()
        { // TODO: Add in the javascript in _layout to check if the tree is connected and render the page accordingly
            var isConnected = _wledClient.IsConnected;
            return new ContentResult() { Content = isConnected.ToString(), ContentType = "text/plain", StatusCode = StatusCodes.Status200OK };
        }
        public async Task<IActionResult> StopAnimation()
        {
            await _wledTaskManager.StopRunningTask();
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> RestoreTreeState()
        {
            await _wledTaskManager.RestoreState();
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> RebootTree()
        {
            await _wledTaskManager.StopRunningTask();
            await _wledTaskManager.Reboot();
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Words()
        {
            await _wledTaskManager.QueueAnimation(_wledPatterns.Words);

            return RedirectToAction("Index");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
