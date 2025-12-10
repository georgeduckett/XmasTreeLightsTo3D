using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Dynamic;
using System.Linq.Expressions;
using System.Numerics;
using System.Threading;
using TreeLightsWeb.BackgroundTaskManagement;
using TreeLightsWeb.Extensions;
using TreeLightsWeb.Models;
using WLEDInterface;
using static WLEDInterface.WledClient;

namespace TreeLightsWeb.Controllers
{
    public class ImageCaptureController : Controller
    {
        private readonly ILogger<ImageCaptureController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IWledClientTaskManager<WledTreeClient> _treeTaskManager;
        private readonly IHubContext<TreeHub> _TreeHubContext;
        private readonly WledTreeClient _treeClient;

        public ImageCaptureController(ILogger<ImageCaptureController> logger, IWebHostEnvironment webHostEnvironment, IWledClientTaskManager<WledTreeClient> treeTaskManager, IHubContext<TreeHub> treeHubContext, WledTreeClient treeClient)
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _treeTaskManager = treeTaskManager;
            _TreeHubContext = treeHubContext;
            _treeClient = treeClient;
        }

        public async Task<IActionResult> Index()
        {
            // Stop any animations if they're running
            await _treeTaskManager.StopRunningTask();

            // Turn on all LEDs
            _treeClient.SetAllLeds(new RGBValue(255, 255, 255));
            await _treeClient.ApplyUpdate(CancellationToken.None);

            return View();
		}

        public async Task<IActionResult> StartImageCapture(string connectionId, int direction, int cameradelay)
        {
            var imagesFolder = Path.Combine(_webHostEnvironment.WebRootPath, "CapturedImages");
            Directory.CreateDirectory(imagesFolder); // Create folder if it doesn't exist

            // Stop any animations if they're running
            await _treeTaskManager.StopRunningTask();

            // Turn on all LEDs
            _treeClient.SetAllLeds(new RGBValue(255, 255, 255));
            await _treeClient.ApplyUpdate(CancellationToken.None);
            await CaptureLEDImage($"AllOn_{direction}");

            // Blank the tree
            _treeClient.SetAllLeds(new RGBValue(0, 0, 0));
            await _treeClient.ApplyUpdate(CancellationToken.None);
            await CaptureLEDImage($"AllOff_{direction}");

            var colourSetObject = (dynamic)new ExpandoObject();
            colourSetObject.seg = (dynamic)new ExpandoObject();

            for (var i = _treeClient.LedIndexEnd - 1; i >= _treeClient.LedIndexStart; i--)
            { // Go backwards as we clear the later indexed led
                var changedLed = false;
                while (!changedLed)
                {
                    try
                    {
                        if (i != _treeClient.LedIndexEnd - 1)
                        {
                            _treeClient.SetLedsColours([new LedUpdate(i, new RGBValue(255, 255, 255)), new LedUpdate(i + 1, new RGBValue(0, 0, 0))]);
                        }
                        else
                        {
                            _treeClient.SetLedsColours([new LedUpdate(i, new RGBValue(255, 255, 255))]);
                        }

                        await _treeClient.ApplyUpdate(CancellationToken.None);
                        changedLed = true;
                    }
                    catch (Exception) { }
                }

                await CaptureLEDImage($"{i}_{direction}", (_treeClient.LedIndexEnd - i) / (float)(_treeClient.LedIndexEnd - _treeClient.LedIndexStart) * 100);
            }

            // Restore the tree to how it was
            await _treeClient.RestoreState();

            return Ok();

            async Task CaptureLEDImage(string fileName, float progressPercent = 0)
            {
                await Task.Delay(cameradelay); // Give the physical LEDs time to change
                var imageResult = await _TreeHubContext.Clients.Client(connectionId).InvokeAsync<string>("CaptureImage", (int)progressPercent, CancellationToken.None);
                imageResult = imageResult.Replace("data:image/png;base64,", string.Empty);

                var fileNameWithPath = Path.Combine(imagesFolder, Path.ChangeExtension(fileName, "png"));

                var fs = new FileStream(fileNameWithPath, FileMode.Create);
                var bw = new BinaryWriter(fs);

                bw.Write(Convert.FromBase64String(imageResult));
                bw.Close();
                fs.Close();
            }
        }

        public async Task<IActionResult> UpdateLEDs(LedUpdate[] ledUpdates)
        {
            await _treeTaskManager.QueueAnimation(async (client, ct) =>
            {
                client.SetLedsColours(ledUpdates);
                await client.ApplyUpdate(ct);
            });

            await Task.Delay(200); // Don't return until the tree has had a chance to change

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
