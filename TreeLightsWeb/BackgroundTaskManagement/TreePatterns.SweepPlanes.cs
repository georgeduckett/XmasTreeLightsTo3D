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
                    await ApplyUpdate(client, cancellationToken, delayAfterMS: 1000);
                    if (cancellationToken.IsCancellationRequested) { break; }

                    var minZ = client.LedCoordinates.Select(coordSelector).Min();
                    var maxZ = client.LedCoordinates.Select(coordSelector).Max();
                    var delta = (maxZ - minZ) / 500;
                    for (var z = minZ; z <= maxZ; z += delta)
                    {
                        Console.Write($"\rSweeping coordinates, {(z - minZ) / (maxZ - minZ):P0}");
                        if (cancellationToken.IsCancellationRequested) { break; }

                        client.SetLedsColours(c => coordSelector(c) <= z + delta ? Colours.White : Colours.Black);
                        await ApplyUpdate(client, cancellationToken);
                    }
                    Console.WriteLine();
                }
            }
        }
    }
}
