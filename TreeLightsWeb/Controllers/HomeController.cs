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
        public async Task<IActionResult> Contagion()
        {
            await _treeTaskManager.QueueTreeAnimation(async (client, ct) =>
            {
                Console.WriteLine("Set all to black");
                client.SetAllLeds(Colours.Black);
                await client.ApplyUpdate();

                // Compute the distance between all LEDs
                var distances = new Dictionary<int, Dictionary<int, float>>();
                for (var i = client.LedIndexStart; i < client.LedIndexEnd; i++)
                {
                    distances[i] = new Dictionary<int, float>();
                    for (var j = client.LedIndexStart; j < client.LedIndexEnd; j++)
                    {
                        // We do it both ways round to sacrifice memory for speed
                        distances[i][j] = Vector3.Distance(client.LedCoordinates[i], client.LedCoordinates[j]);
                    }
                }

                var usePrimaryColour = true;
                var litIndexes = new HashSet<int>();
                var unlitIndexes = new HashSet<int>(Enumerable.Range(client.LedIndexStart, client.LedIndexEnd - client.LedIndexStart));
                var rand = new Random();
                while (!ct.IsCancellationRequested)
                {

                    // First choose a random LED to start the contagion
                    var nextLedIndex = new Random().Next(client.LedIndexStart, client.LedIndexEnd);

                    client.SetLedColour(nextLedIndex, usePrimaryColour ? Colours.Red : Colours.Green);
                    await client.ApplyUpdate();

                    litIndexes.Add(nextLedIndex);
                    unlitIndexes.Remove(nextLedIndex);

                    while (!ct.IsCancellationRequested && unlitIndexes.Count != 0)
                    {
                        if (ct.IsCancellationRequested) { break; }

                        // Find the closest unlit LED to the current lit LEDs (randomly choose from the top 5)
                        var closestUnlitLedIndex = unlitIndexes.OrderBy(unlitIndex => litIndexes.Min(litIndex => distances[litIndex][unlitIndex])).Take(5).RandomElement(rand);

                        client.SetLedColour(closestUnlitLedIndex, usePrimaryColour ? Colours.Red : Colours.Green);
                        litIndexes.Add(closestUnlitLedIndex);
                        unlitIndexes.Remove(closestUnlitLedIndex);

                        await client.ApplyUpdate();

                        try
                        {
                            await Task.Delay(50, ct);
                        }
                        catch (TaskCanceledException) { break; }
                    }
                    usePrimaryColour = !usePrimaryColour;
                    // Swap the hashsets around
                    (unlitIndexes, litIndexes) = (litIndexes, unlitIndexes);
                }
            });
            return RedirectToAction("Index");
        }
        public async Task<IActionResult> RotateDynamic()
        {
            await _treeTaskManager.QueueTreeAnimation(async (client, ct) =>
            {
                Console.WriteLine("Set all to black");
                client.SetAllLeds(Colours.Black);
                await client.ApplyUpdate();

                var yaw = 0.0f;
                var pitch = 0.0f;
                var roll = 0.0f;
                while (!ct.IsCancellationRequested)
                {
                    if (ct.IsCancellationRequested) { break; }

                    client.SetLedsColours(c => Vector3.Transform(c - new Vector3(0, 0, client.LedCoordinates.Max(c => c.Z) / 2), Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll)).X >= 0 ? Colours.Red : Colours.Green);
                    await client.ApplyUpdate();
                    yaw += (float)(180 / Math.PI) / 500;
                    pitch += (float)(180 / Math.PI) / 4800;
                    // Only rodate around 2 axis as it doesn't look as good around 3
                    //roll += (float)(180 / Math.PI) / 600;
                }
            });
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

        public async Task<IActionResult> Ball()
        {
            await _treeTaskManager.QueueTreeAnimation(async (client, ct) =>
            {
                client.SetAllLeds(Colours.Black);
                await client.ApplyUpdate();

                var ballSize = 0.35f;
                var minBallX = client.LedCoordinates.Min(c => c.X);
                var maxBallX = client.LedCoordinates.Max(c => c.X);
                var minBallY = client.LedCoordinates.Min(c => c.Y);
                var maxBallY = client.LedCoordinates.Max(c => c.Y);
                var minBallZ = client.LedCoordinates.Min(c => c.Z);
                var maxBallZ = client.LedCoordinates.Max(c => c.Z);
                
                var rand = new Random();
                var ballCoords = Enumerable.Range(0, 3).Select(i => new Vector3(rand.NextSingle() * (maxBallX - minBallX) + minBallX,
                                             rand.NextSingle() * (maxBallX - minBallY) + minBallY,
                                             rand.NextSingle() * (maxBallX - minBallZ) + minBallZ)).ToArray();

                var ballBaseSpeed = 0.025f;
                var ballSpeed = Enumerable.Range(0, 3).Select(i => new Vector3(i == 0 ? -ballBaseSpeed : ballBaseSpeed, i == 1 ? -ballBaseSpeed : ballBaseSpeed, i == 2 ? -ballBaseSpeed : ballBaseSpeed)).ToArray();

                var ballColour = new[] { Colours.Red, Colours.Green, Colours.Blue };

                while (!ct.IsCancellationRequested)
                {
                    // TODO: Make the ball bounce off of a cone around the tree, not a cuboid
                    for (int ballIndex = 0; ballIndex < ballCoords.Length; ballIndex++)
                    {
                        ballCoords[ballIndex] += ballSpeed[ballIndex];

                        if (ballCoords[ballIndex].X > maxBallX)
                        {
                            ballSpeed[ballIndex].X = -Math.Abs(ballSpeed[ballIndex].X);
                        }

                        if (ballCoords[ballIndex].X < minBallX)
                        {
                            ballSpeed[ballIndex].X = Math.Abs(ballSpeed[ballIndex].X);
                        }

                        if (ballCoords[ballIndex].Y > maxBallY)
                        {
                            ballSpeed[ballIndex].Y = -Math.Abs(ballSpeed[ballIndex].Y);
                        }

                        if (ballCoords[ballIndex].Y < minBallY)
                        {
                            ballSpeed[ballIndex].Y = Math.Abs(ballSpeed[ballIndex].Y);
                        }

                        if (ballCoords[ballIndex].Z > maxBallZ)
                        {
                            ballSpeed[ballIndex].Z = -Math.Abs(ballSpeed[ballIndex].Z);
                        }
                        if (ballCoords[ballIndex].Z < minBallZ)
                        {
                            ballSpeed[ballIndex].Z = Math.Abs(ballSpeed[ballIndex].Z);
                        }
                    }

                    client.SetLedsColours(c => Enumerable.Range(0, ballCoords.Length)
                                                         .Select(b => Vector3.Distance(c, ballCoords[b]) < ballSize ?
                                                                      ballColour[b] * (1 - (Vector3.Distance(c, ballCoords[b]) / ballSize)) :
                                                                      Colours.Black)
                                                         .Aggregate((l, r) => l + r));

                    await client.ApplyUpdate();

                    try
                    {
                        await Task.Delay(50, ct);
                    }
                    catch (TaskCanceledException) { break; }
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
                    client.SetAllLeds(Colours.Black);
                    await client.ApplyUpdate();

                    for (var bi = 0; bi < binary.Max(bString => bString.Length); bi++)
                    {
                        if (ct.IsCancellationRequested) { break; }

                        client.SetLedsColours((i, c) => bi >= binary[i].Length || binary[i][bi] == '0' ? Colours.Red : Colours.White);
                        await client.ApplyUpdate();
                        try
                        {
                            await Task.Delay(1000, ct);
                        }
                        catch (TaskCanceledException) { break; }
                    }
                    try
                    {
                        await Task.Delay(4000, ct);
                    }
                    catch (TaskCanceledException) { break; }
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
