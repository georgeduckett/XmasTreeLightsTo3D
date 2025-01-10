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
    public class ImageCaptureController : Controller
    {
        private readonly ILogger<ImageCaptureController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ImageCaptureController(ILogger<ImageCaptureController> logger, IWebHostEnvironment webHostEnvironment)
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
        }

        public IActionResult Index()
        {
            return View();
		}

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
