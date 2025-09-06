using System.Numerics;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        public async ValueTask RotateAroundAxis(WledTreeClient client, CancellationToken cancellationToken)
        {
            Func<float, Quaternion>[] rotationDirections = [angle => Quaternion.CreateFromYawPitchRoll(angle, 0, 0), angle => Quaternion.CreateFromYawPitchRoll(0, angle, 0), angle => Quaternion.CreateFromYawPitchRoll(0, 0, angle)];
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var rotationDir in rotationDirections)
                {
                    Console.WriteLine("Set all to black");
                    client.SetAllLeds(Colours.Black);
                    await ApplyUpdate(client, cancellationToken);
                    if (cancellationToken.IsCancellationRequested) { break; }
                    var rotationAngle = 0.0f;
                    while (rotationAngle < 300)
                    {
                        if (cancellationToken.IsCancellationRequested) { break; }

                        client.SetLedsColours(c => Vector3.Transform(c - new Vector3(0, 0, client.LedCoordinates.Max(c => c.Z) / 2), rotationDir(rotationAngle)).X >= 0 ? Colours.Red : Colours.Green);
                        await ApplyUpdate(client, cancellationToken);
                        rotationAngle += (float)(180 / Math.PI) / 500;
                    }

                }
            }
        }
    }
}
