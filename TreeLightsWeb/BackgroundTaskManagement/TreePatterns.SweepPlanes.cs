using System.Diagnostics;
using System.Numerics;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        public async ValueTask SweepPlanes(WledTreeClient client, CancellationToken cancellationToken)
        {
            Console.WriteLine("Sweep a plane in each direction");
            var coordSelectors = new[] { new Func<Vector3, double>(c => c.Z), new Func<Vector3, double>(c => c.X), new Func<Vector3, double>(c => c.Y) };
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var coordSelector in coordSelectors)
                {
                    Console.WriteLine("Set all to black");
                    client.SetAllLeds(Colours.Black);
                    await ApplyUpdate(client, cancellationToken, delayAfterMS: Math.Min(1000, ScaleDelay(1000, _settings.SpeedFor(nameof(SweepPlanes)))));
                    if (cancellationToken.IsCancellationRequested) { break; }

                    var minZ = client.LedCoordinates.Select(coordSelector).Min();
                    var maxZ = client.LedCoordinates.Select(coordSelector).Max();
                    var band = (maxZ - minZ) / 500;
                    var clock = Stopwatch.StartNew();
                    var previous = 0.0;
                    var progress = 0.0;
                    while (progress <= 1.0)
                    {
                        Console.Write($"\rSweeping coordinates, {Math.Min(1, progress):P0}");
                        if (cancellationToken.IsCancellationRequested) { break; }

                        var z = minZ + (maxZ - minZ) * progress;
                        client.SetLedsColours(c => coordSelector(c) <= z + band ? Colours.White : Colours.Black);
                        await ApplyUpdate(client, cancellationToken, delayAfterMS: 50);

                        var now = clock.Elapsed.TotalSeconds;
                        var dt = Math.Clamp(now - previous, 0, 0.1);
                        previous = now;
                        progress += dt * _settings.SpeedFor(nameof(SweepPlanes)) / 25.0;
                    }
                    Console.WriteLine();
                }
            }
        }
    }
}
