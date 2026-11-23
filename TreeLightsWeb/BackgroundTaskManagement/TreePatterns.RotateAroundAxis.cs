using System.Diagnostics;
using System.Numerics;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        /// <summary>How fast the rotating animations turn, in radians a second.</summary>
        private const float RotationSpeed = 2.5f;

        public async ValueTask RotateAroundAxis(WledTreeClient client, CancellationToken cancellationToken)
        {
            // Each rotation, with the component of the turned position that the red/green split tests: a rotation about the
            // X axis (pitch) doesn't change X, so that one tests Y
            (Func<float, Quaternion> Rotation, Func<Vector3, float> Side)[] rotationDirections =
            [
                (angle => Quaternion.CreateFromYawPitchRoll(angle, 0, 0), v => v.X),
                (angle => Quaternion.CreateFromYawPitchRoll(0, angle, 0), v => v.Y),
                (angle => Quaternion.CreateFromYawPitchRoll(0, 0, angle), v => v.X),
            ];
            var centre = new Vector3(0, 0, client.LedCoordinates.Max(c => c.Z) / 2);
            var colours = _settings.ColoursFor(RotateAroundAxisColours);
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var (rotation, side) in rotationDirections)
                {
                    Console.WriteLine("Set all to black");
                    client.SetAllLeds(Colours.Black);
                    await ApplyUpdate(client, cancellationToken);
                    if (cancellationToken.IsCancellationRequested) { break; }
                    // The angle comes from the time, so it turns at the same speed however fast the tree can be updated
                    var clock = Stopwatch.StartNew();
                    var rotationAngle = 0.0f;
                    while (rotationAngle < 300)
                    {
                        if (cancellationToken.IsCancellationRequested) { break; }

                        var turn = rotation(rotationAngle);
                        // The palette's first two colours (red and green at Christmas), or its own colours
                        var (first, second) = (colours["One half"].ToRGBValue(), colours["Other half"].ToRGBValue());
                        client.SetLedsColours(c => side(Vector3.Transform(c - centre, turn)) >= 0 ? first : second);
                        await ApplyUpdate(client, cancellationToken, delayAfterMS: 20);
                        rotationAngle = (float)clock.Elapsed.TotalSeconds * RotationSpeed;
                    }

                }
            }
        }
    }
}
