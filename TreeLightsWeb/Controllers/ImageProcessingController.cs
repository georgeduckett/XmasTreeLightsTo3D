using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OpenCvSharp;
using TreeLightsWeb.BackgroundTaskManagement;
using TreeLightsWeb.ImageProcessing;
using TreeLightsWeb.Models;
using WLEDInterface;

namespace TreeLightsWeb.Controllers
{
    public class ImageProcessingController : Controller
    {
        private readonly ILogger<ImageCaptureController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ITreeTaskManager _treeTaskManager;
        private readonly WledTreeClient _treeClient;
        private readonly IHubContext<TreeHub> _TreeHubContext;

        public ImageProcessingController(ILogger<ImageCaptureController> logger, IWebHostEnvironment webHostEnvironment, ITreeTaskManager treeTaskManager, IHubContext<TreeHub> treeHubContext, WledTreeClient wledTreeClient     )
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _treeTaskManager = treeTaskManager;
            _TreeHubContext = treeHubContext;
            _treeClient = wledTreeClient;
        }

        public IActionResult Index() => View();

        public IActionResult CoordinateCorrection() => View();

        [HttpPost]
        public async Task<IActionResult> StartImageProcessing(string connectionId, [FromBody] ImageProcessingModel model)
        {
            model.LEDCount = _treeClient.LedIndexEnd - _treeClient.LedIndexStart;
            model.WebRootFolder = _webHostEnvironment.WebRootPath;
            var imageProcesor = new ImageProcessor(model);

            await imageProcesor.ProcessImages(async (s) => await UpdateClient(connectionId, s));

            return Ok();

            async Task UpdateClient(string connectionId, string progress)
            {
                if (progress.StartsWith("\r"))
                {
                    await _TreeHubContext.Clients.Client(connectionId).SendAsync("UpdateProcessingProgress", $"\r{DateTime.Now.ToLongTimeString()} {progress[1..]}");
                }
                else
                {
                    await _TreeHubContext.Clients.Client(connectionId).SendAsync("UpdateProcessingProgress", DateTime.Now.ToLongTimeString() + " " + progress);
                }
            }
        }

        public async Task<IActionResult> DownloadCoordinatesFile()
        {
            if (System.IO.File.Exists(Path.Combine(_webHostEnvironment.WebRootPath, "coordinates.csv")))
            {
                var memory = new MemoryStream();
                using (var stream = new FileStream(Path.Combine(_webHostEnvironment.WebRootPath, "coordinates.csv"), FileMode.Open))
                {
                    await stream.CopyToAsync(memory);
                }
                memory.Position = 0;
                return File(memory, "text/plain", "coordinates.csv");
            }
            else
            {
                return NotFound();
            }
        }

        public async Task<IActionResult> UploadCoordinatesFile(IFormFile coordinatesFile)
        {
            if (coordinatesFile == null || coordinatesFile.Length == 0)
            {
                return BadRequest("No file uploaded");
            }
            // TODO: Do some kind of validation on the file
            var filePath = Path.Combine(_webHostEnvironment.WebRootPath, "coordinates.csv");
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await coordinatesFile.CopyToAsync(stream);
            }
            return RedirectToAction(nameof(Index), nameof(ImageProcessingController).Replace("Controller", ""));
        }
    }
}
