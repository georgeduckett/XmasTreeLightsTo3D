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

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment webHostEnvironment, ITreeTaskManager treeTaskManager)
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _treeTaskManager = treeTaskManager;
        }

        public IActionResult Index()
        {
            return View();
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
            await _treeTaskManager.QueueTreeAnimation(TreePatterns.Contagion);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> RotateDynamic()
        {
            await _treeTaskManager.QueueTreeAnimation(TreePatterns.RotateDynamic);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> RotateAroundAxis()
        {
            await _treeTaskManager.QueueTreeAnimation(TreePatterns.RotateAroundAxis);
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Snake()
        {
            await _treeTaskManager.QueueTreeAnimation(TreePatterns.Snake);

            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Balls()
        {
            await _treeTaskManager.QueueTreeAnimation(TreePatterns.Balls);
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> SweepPlaneInEachDirection()
        {
            await _treeTaskManager.QueueTreeAnimation(TreePatterns.SweepPlanes);

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Binary()
        {
            await _treeTaskManager.QueueTreeAnimation(TreePatterns.Binary);

            return RedirectToAction("Index");
        }

        public IActionResult Privacy()
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
