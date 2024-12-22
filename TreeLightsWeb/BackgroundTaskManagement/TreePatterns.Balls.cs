using System.Numerics;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public static partial class TreePatterns
    {
        public static async ValueTask Balls(WledTreeClient client, CancellationToken cancellationToken)
        {
            client.SetAllLeds(Colours.Black);
            await client.ApplyUpdate();

            var ballSize = 0.35f;
            var minBallX = -1;
            var maxBallX = 1;
            var minBallY = -1;
            var maxBallY = 1;
            var minBallZ = 0;
            var maxBallZ = client.LedCoordinates.Max(c => c.Z);

            var rand = new Random();
            var ballCoords = Enumerable.Range(0, 3).Select(i => new Vector3(rand.NextSingle() * (maxBallX - minBallX) + minBallX,
                                         rand.NextSingle() * (maxBallX - minBallY) + minBallY,
                                         rand.NextSingle() * (maxBallX - minBallZ) + minBallZ)).ToArray();

            var ballBaseSpeed = 0.025f;
            var ballSpeed = Enumerable.Range(0, 3).Select(i => new Vector3(i == 0 ? -ballBaseSpeed : ballBaseSpeed, i == 1 ? -ballBaseSpeed : ballBaseSpeed, i == 2 ? -ballBaseSpeed : ballBaseSpeed)).ToArray();

            var ballColour = new[] { Colours.Red, Colours.Green, Colours.Blue };

            while (!cancellationToken.IsCancellationRequested)
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

                await DelayNoException(50, cancellationToken);
            }
        }
    }
}
