using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using TreeLightsWeb.BackgroundTaskManagement;
using TreeLightsWeb.Extensions;
using TreeLightsWeb.Models;
using WLEDInterface;
using static WLEDInterface.WledTreeClient;

namespace TreeLightsWeb.Controllers
{
    public class ImageCaptureController : Controller
    {
        private readonly ILogger<ImageCaptureController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ITreeTaskManager _treeTaskManager;

        public ImageCaptureController(ILogger<ImageCaptureController> logger, IWebHostEnvironment webHostEnvironment, ITreeTaskManager treeTaskManager)
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _treeTaskManager = treeTaskManager;
        }

        public IActionResult Index()
        {
            return View();
		}

        public async Task<IActionResult> StartImageCapture()
        {
            await _treeTaskManager.QueueTreeAnimation(async (client, ct) =>
            {
                // Blank the tree
                client.SetAllLeds(new RGBValue(0, 0, 0));
                await client.ApplyUpdate(ct);
            });

            return Ok();
        }

        public async Task<IActionResult> UpdateLEDs(LedUpdate[] ledUpdates)
        {
            await _treeTaskManager.QueueTreeAnimation(async (client, ct) =>
            {
                client.SetLedsColours(ledUpdates);
                await client.ApplyUpdate(ct);
            });

            return Ok();
        }

        public async Task<IActionResult> EndImageCapture()
        {
            // TODO: Replace any previously captured images with the ones we've been capturing this run


            return Ok();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
