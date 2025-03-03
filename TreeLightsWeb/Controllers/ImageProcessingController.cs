﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TreeLightsWeb.BackgroundTaskManagement;
using TreeLightsWeb.Models;

namespace TreeLightsWeb.Controllers
{
    public class ImageProcessingController : Controller
    {
        private readonly ILogger<ImageCaptureController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ITreeTaskManager _treeTaskManager;
        private readonly IHubContext<TreeHub> _TreeHubContext;

        public ImageProcessingController(ILogger<ImageCaptureController> logger, IWebHostEnvironment webHostEnvironment, ITreeTaskManager treeTaskManager, IHubContext<TreeHub> treeHubContext)
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _treeTaskManager = treeTaskManager;
            _TreeHubContext = treeHubContext;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> StartImageProcessing(string connectionId, [FromBody] ImageProcessingModel model)
        {
            await UpdateClient(connectionId, "Started");
            await Task.Delay(5000);
            await UpdateClient(connectionId, "Finished");

            return Ok();

            async Task UpdateClient(string connectionId, string progress)
            {
                await _TreeHubContext.Clients.Client(connectionId).SendAsync("UpdateProcessingProgress", DateTime.Now.ToLongTimeString() + " " + progress);
            }
        }
    }
}
