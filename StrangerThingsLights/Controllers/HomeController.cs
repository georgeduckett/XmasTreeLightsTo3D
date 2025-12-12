using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using StrangerThingsLights.BackgroundTaskManagement;
using StrangerThingsLights.Models;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
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

        public async Task<IActionResult> ReconnectToTree()
        {
            await _wledTaskManager.Reconnect();
            return RedirectToAction("Index");
        }
        public IActionResult IsTreeConnected()
        {
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

        public async Task<IActionResult> Words(string wordToDisplay)
        {
            LightsLayoutModel? model = null;
            if (System.IO.File.Exists(Path.Combine(_webHostEnvironment.WebRootPath, "Config", "LightsLayoutModel.json")))
            {
                model = JsonConvert.DeserializeObject<LightsLayoutModel>(System.IO.File.ReadAllText(Path.Combine(_webHostEnvironment.WebRootPath, "Config", "LightsLayoutModel.json")));
            }
            else
            {
                var startLetter = (int)'a';
                model = new LightsLayoutModel()
                {
                    LetterMappings = Enumerable.Range(0, 26).Select(i => new LightsLayoutModel.LetterMapping((char)(startLetter + i), (ushort)i)).ToArray()
                };
            }

            model!.Validate();

            await _wledTaskManager.QueueAnimation((c, ct) => _wledPatterns.Words(c, model!, wordToDisplay, ct));

            return RedirectToAction("Index");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
