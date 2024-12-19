using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Numerics;
using TreeLightsWeb.BackgroundTaskManagement;
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

        public async Task<IActionResult> Index()
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

        public async Task<IActionResult> RotateAroundAxis()
        {
            await _treeTaskManager.QueueTreeAnimation(async (client, ct) =>
            {
                Func<float, Quaternion>[] rotationDirections = [angle => Quaternion.CreateFromYawPitchRoll(angle, 0, 0), angle => Quaternion.CreateFromYawPitchRoll(0, angle, 0), angle => Quaternion.CreateFromYawPitchRoll(0, 0, angle)];
                while (!ct.IsCancellationRequested)
                {
                    foreach (var rotationDir in rotationDirections)
                    {
                        Console.WriteLine("Set all to black");
                        client.SetAllLeds(Colours.Black);
                        await client.ApplyUpdate();
                        if (ct.IsCancellationRequested) { break; }
                        var rotationAngle = 0.0f;
                        while (rotationAngle < 300)
                        {
                            if (ct.IsCancellationRequested) { break; }

                            client.SetLedsColours(c => Vector3.Transform(c - new Vector3(0, 0, client.LedCoordinates.Max(c => c.Z) / 2), rotationDir(rotationAngle)).X >= 0 ? Colours.Red : Colours.Green);
                            await client.ApplyUpdate();
                            rotationAngle += (float)(180 / Math.PI) / 500;
                        }

                    }
                }
            });
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> SweepPlaneInEachDirection()
        {
            await _treeTaskManager.QueueTreeAnimation(async (client, ct) =>
            {
                Console.WriteLine("Sweep a plane in each direction");
                var coordSelectors = new[] { new Func<Vector3, double>(c => c.Z), new Func<Vector3, double>(c => c.X), new Func<Vector3, double>(c => c.Y) };
                while (!ct.IsCancellationRequested)
                {
                    foreach (var coordSelector in coordSelectors)
                    {
                        Console.WriteLine("Set all to black");
                        client.SetAllLeds(Colours.Black);
                        await client.ApplyUpdate();
                        if (ct.IsCancellationRequested) { break; }
                        await Task.Delay(5000, ct);

                        var minZ = client.LedCoordinates.Select(coordSelector).Min();
                        var maxZ = client.LedCoordinates.Select(coordSelector).Max();
                        var delta = (maxZ - minZ) / 500;
                        for (var z = minZ; z <= maxZ; z += delta)
                        {
                            Console.Write($"\rSweeping coordinates, {(z - minZ) / (maxZ - minZ):P0}");
                            if (ct.IsCancellationRequested) { break; }

                            client.SetLedsColours(c => coordSelector(c) <= z + delta ? Colours.White : Colours.Black);
                            await client.ApplyUpdate();
                        }
                        Console.WriteLine();
                    }
                }
            });

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> FlashBinaryIndexes()
        {
            await _treeTaskManager.QueueTreeAnimation(async (client, ct) =>
            {
                Console.WriteLine("Flashing indexes in binary");
                // Convert all numbers to binary
                var binary = Enumerable.Range(client.LedIndexStart, client.LedIndexEnd - client.LedIndexStart)
                    .Select(i => new string(Convert.ToString(i, 2).Reverse().ToArray())).ToArray();
                while (!ct.IsCancellationRequested)
                {
                    if (ct.IsCancellationRequested) { break; }

                    for (int reps = 0; reps < 1; reps++)
                    {
                        if (reps != 0)
                        {
                            await Task.Delay(5000, ct);
                        }

                        for (var i = 0; i < binary.Max(index => index.Length); i++)
                        {
                            if (ct.IsCancellationRequested) { break; }

                            client.SetLedsColours(c => binary.Any(index => index.Length > i && index[index.Length - i - 1] == '1' && index.Length - i - 1 < index.Length) ? Colours.White : Colours.Black);
                            await client.ApplyUpdate();
                        }
                    }
                }
            });

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
