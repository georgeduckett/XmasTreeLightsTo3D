using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TreeLightsWeb.ImageCapture;
using TreeLightsWeb.Models;
using WLEDInterface;
using static WLEDInterface.WledClient;

namespace TreeLightsWeb.Controllers
{
    public class ImageCaptureController : Controller
    {
        private readonly ILogger<ImageCaptureController> _logger;
        private readonly IWledClientTaskManager<WledTreeClient> _treeTaskManager;
        private readonly ImageCaptureCoordinator _coordinator;

        public ImageCaptureController(ILogger<ImageCaptureController> logger, IWledClientTaskManager<WledTreeClient> treeTaskManager, ImageCaptureCoordinator coordinator)
        {
            _logger = logger;
            _treeTaskManager = treeTaskManager;
            _coordinator = coordinator;
        }

        public IActionResult Index()
        {
            // Light the whole tree so the camera can be lined up (unless a capture is under way, e.g. this page was reloaded
            // part way through one). Not awaited, so the page loads straight away even if the tree is slow to respond.
            _ = LightTreeForFramingAsync();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> LightTree()
        {
            return await _coordinator.LightTreeForFramingAsync()
                ? Ok()
                : Conflict(new { error = "Can't light the tree while a capture is running." });
        }

        [HttpPost]
        public async Task<IActionResult> Start([FromBody] StartCaptureRequest request)
        {
            var result = await _coordinator.StartCaptureAsync(request);
            return result.Ok ? Ok(result) : result.Code == "invalid" ? BadRequest(result) : Conflict(result);
        }

        [HttpPost]
        public async Task<IActionResult> MeasureLatency([FromBody] MeasureLatencyRequest request)
        {
            var result = await _coordinator.MeasureLatencyAsync(request);
            return result.Ok ? Ok(result) : result.Code == "invalid" ? BadRequest(result) : Conflict(result);
        }

        [HttpPost]
        public IActionResult Stop()
        {
            _coordinator.Stop();
            return Ok();
        }

        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Status() => Json(_coordinator.Status);

        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Summary() => Json(_coordinator.GetSummary());

        /// <summary>
        /// The capture page posts each frame here (the raw PNG as the request body) once it's grabbed it, while the next LED
        /// is being captured.
        /// </summary>
        [HttpPost]
        [RequestSizeLimit(100_000_000)]
        public async Task<IActionResult> UploadImage(string jobId, long seq, string name)
        {
            if (string.IsNullOrEmpty(jobId) || string.IsNullOrEmpty(name))
            {
                return BadRequest(new { error = "jobId and name are required." });
            }

            var outcome = await _coordinator.SaveUploadAsync(jobId, seq, name, Request.Body, HttpContext.RequestAborted);
            return outcome.StatusCode == StatusCodes.Status200OK
                ? Ok()
                : StatusCode(outcome.StatusCode, new { error = outcome.Error });
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

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private async Task LightTreeForFramingAsync()
        {
            try
            {
                await _coordinator.LightTreeForFramingAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Couldn't light the tree for framing");
            }
        }
    }
}
