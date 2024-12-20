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
        public async Task<IActionResult> Snake()
        {
            await _treeTaskManager.QueueTreeAnimation(async (client, ct) =>
            {
                client.SetAllLeds(Colours.Black);
                await client.ApplyUpdate();

                // Get the (unnormalised) direction from i to j for each point.
                var directions = new Dictionary<int, Dictionary<int, Vector3>>();
                for (var i = client.LedIndexStart; i < client.LedIndexEnd; i++)
                {
                    var ledDistances = new Dictionary<int, Vector3>();
                    for (var j = client.LedIndexStart + 1; j < client.LedIndexEnd; j++)
                    {
                        if (i != j)
                        {
                            // We do it both ways round to sacrifice memory for speed
                            ledDistances[j] = client.LedCoordinates[j] - client.LedCoordinates[i];
                        }
                    }

                    var orderedDistances = ledDistances.OrderBy(kv => kv.Value.Length()).ToArray();

                    // We need to try to have not just the closest 5, but 5 that are in different directions, while also being close
                    // For each led, keep adding the closest led that isn't in the same direction as the previous ones
                    var bestConnections = new Dictionary<int, Vector3>
                    {
                        { orderedDistances[0].Key, orderedDistances[0].Value }
                    };
                    for (var j = 1; j < orderedDistances.Length; j++)
                    {
                        if (bestConnections.Count == 6) { break; }

                        var isGood = true;
                        foreach (var connection in bestConnections)
                        {
                            if (Math.Abs(Math.Acos(Vector3.Dot(Vector3.Normalize(connection.Value), Vector3.Normalize(orderedDistances[j].Value)))) < Math.PI / 4)
                            { // we found a connection that is in the same direction as one of the ones we already have so don't use it
                                isGood = false;
                                break;
                            }
                        }

                        if (isGood)
                        {
                            bestConnections.Add(orderedDistances[j].Key, orderedDistances[j].Value);
                        }
                    }

                    directions[i] = bestConnections;
                }


                // Now that we've got the best connections for each index normalize the vectors to make things easier later
                foreach (var direction in directions)
                {
                    foreach (var connection in direction.Value)
                    {
                        directions[direction.Key][connection.Key] = Vector3.Normalize(connection.Value);
                    }
                }

                while (!ct.IsCancellationRequested)
                {
                    var freeLeds = new HashSet<int>(Enumerable.Range(client.LedIndexStart, client.LedIndexEnd - client.LedIndexStart));
                    var snakeLength = 2;
                    var snakeLeds = new List<int> { Random.Shared.Next(client.LedIndexStart, client.LedIndexEnd) };
                    freeLeds.RemoveWhere(i => snakeLeds.Contains(i));

                    var appleLocation = freeLeds.RandomElement(Random.Shared);

                    client.SetLedsColours(snakeLeds.Select(i => new WledTreeClient.LedUpdate(i, Colours.White)).ToArray());
                    client.SetLedColour(appleLocation, Colours.Red);
                    await client.ApplyUpdate();
                    await DelayNoException(100, ct);
                    var iterationsWithNoApple = 0;
                    while (!ct.IsCancellationRequested)
                    {
                        // Decide where to move the snake next
                        var possibleMoves = directions[snakeLeds[0]].Where(kv => freeLeds.Contains(kv.Key)).ToArray();

                        if (possibleMoves.Length == 0)
                        { // Snake has no where to go so we're done moving the snake
                            break;
                        }
                        else
                        {
                            // Get the 'best' possible move based on moving in the direction of the apple
                            var snakeToApple = Vector3.Normalize(client.LedCoordinates[appleLocation] - client.LedCoordinates[snakeLeds[0]]);
                            var bestMove = possibleMoves.OrderBy(kv => Math.Abs(Math.Acos(Vector3.Dot(kv.Value, snakeToApple)))).First();

                            client.SetLedColour(snakeLeds[0], Colours.Green);
                            snakeLeds.Insert(0, bestMove.Key);
                            freeLeds.Remove(bestMove.Key);
                            client.SetLedColour(snakeLeds[0], Colours.White);
                            for (int i = 1; i < snakeLeds.Count; i++)
                            {
                                client.SetLedColour(snakeLeds[i], new RGBValue(0, (byte)Math.Ceiling((1 - (i / Math.Max(10.0, snakeLeds.Count - 1))) * (255 - 60) + 60), 0));
                            }

                            if (bestMove.Key == appleLocation)
                            {
                                iterationsWithNoApple = 0;
                                snakeLength++;
                                appleLocation = freeLeds.RandomElement(Random.Shared);
                                client.SetLedColour(appleLocation, Colours.Red);
                            }
                            else if(iterationsWithNoApple > 250)
                            {
                                // We keep moving apples and still can't get them, so just break out of the loop and start again
                                break;
                            }
                            else if (iterationsWithNoApple++ == 50)
                            { // We've gone too long without an apple so just change the location randomly
                                client.SetLedColour(appleLocation, Colours.Black);
                                appleLocation = freeLeds.RandomElement(Random.Shared);
                                client.SetLedColour(appleLocation, Colours.Red);
                            }

                            if (snakeLeds.Count > snakeLength)
                            {
                                client.SetLedColour(snakeLeds.Last(), Colours.Black);
                                freeLeds.Add(snakeLeds[snakeLeds.Count - 1]);
                                snakeLeds.RemoveAt(snakeLeds.Count - 1);
                            }
                        }

                        await DelayNoException(180, ct);
                        await client.ApplyUpdate();
                    }

                    if (!ct.IsCancellationRequested)
                    {
                        // Clear the board
                        foreach (var snakeLEDIndex in snakeLeds)
                        {
                            await DelayNoException(150, ct);
                            client.SetLedColour(snakeLEDIndex, Colours.Black);
                            await client.ApplyUpdate();
                        }
                        // Remove the apple
                        await DelayNoException(2000, ct);
                        client.SetLedColour(appleLocation, Colours.Black);
                        await client.ApplyUpdate();
                    }


                    // Wait to restart
                    await DelayNoException(5000, ct);
                }
            });

            return RedirectToAction("Index");
        }
        private async Task DelayNoException(int delay, CancellationToken ct)
        {
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException) { }
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
