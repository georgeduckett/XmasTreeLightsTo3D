using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using OpenCvSharp;
using System.Reflection.Metadata.Ecma335;
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

        public IActionResult Index()
        {
            ImageProcessingModel? model = null;
            if (System.IO.File.Exists(Path.Combine(_webHostEnvironment.WebRootPath, "ImageProcessingModel.json")))
            {
                model = JsonConvert.DeserializeObject<ImageProcessingModel>(System.IO.File.ReadAllText(Path.Combine(_webHostEnvironment.WebRootPath, "ImageProcessingModel.json")));
            }
            return View(model ?? new ImageProcessingModel());
        }

        public IActionResult CoordinateCorrection() => View();
         
        [HttpGet]
        public IActionResult GetStoredImageData(int ledIndex, int treeAngleIndex)
        {
            var imagePath = Path.Combine(_webHostEnvironment.WebRootPath, "CapturedImages", $"{ledIndex}_{treeAngleIndex * 45}.png");
            var result = ImageProcessor.GetMinMaxLoc(imagePath);
            // Can't use return JSON(...) because it doesn't serialize the OpenCV types
            return new ContentResult() { Content = JsonConvert.SerializeObject(result), ContentType = "application/json", StatusCode = StatusCodes.Status200OK };
        }

        [HttpPost]
        public async Task<IActionResult> StartImageProcessing(string connectionId, [FromBody] ImageProcessingModel model)
        {
            model.LEDCount = _treeClient.LedIndexEnd - _treeClient.LedIndexStart;
            model.WebRootFolder = _webHostEnvironment.WebRootPath;

            await System.IO.File.WriteAllTextAsync(Path.Combine(_webHostEnvironment.WebRootPath, "ImageProcessingModel.json"), JsonConvert.SerializeObject(model));

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
            if (System.IO.File.Exists(Path.Combine(_webHostEnvironment.WebRootPath, "Config", "coordinates.csv")))
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
