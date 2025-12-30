using System.Numerics;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        public async ValueTask Balls(WledTreeClient client, CancellationToken cancellationToken)
        {
            client.SetAllLeds(Colours.Black);
            await ApplyUpdate(client, cancellationToken);

            var ballSize = 0.45f;
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

            var ballBaseSpeed = 0.015f;
            var ballSpeed = Enumerable.Range(0, 3).Select(i => new Vector3(i == 0 ? -ballBaseSpeed : ballBaseSpeed, i == 1 ? -ballBaseSpeed : ballBaseSpeed, i == 2 ? -ballBaseSpeed : ballBaseSpeed)).ToArray();

            var ballColour = new[] { Colours.Red, Colours.Green, Colours.Blue };

            while (!cancellationToken.IsCancellationRequested)
            {
                for (int ballIndex = 0; ballIndex < ballCoords.Length; ballIndex++)
                {
                    ballCoords[ballIndex] += ballSpeed[ballIndex];
                    // We reduce the max X & Y by how far up the tree we are so we bounce off the cone surrounding the tree
                    if (ballCoords[ballIndex].X > maxBallX - (ballCoords[ballIndex].Z / maxBallZ))
                    {
                        ballSpeed[ballIndex].X = -Math.Abs(ballSpeed[ballIndex].X);
                    }

                    if (ballCoords[ballIndex].X < minBallX + (ballCoords[ballIndex].Z / maxBallZ))
                    {
                        ballSpeed[ballIndex].X = Math.Abs(ballSpeed[ballIndex].X);
                    }

                    if (ballCoords[ballIndex].Y > maxBallY - (ballCoords[ballIndex].Z / maxBallZ))
                    {
                        ballSpeed[ballIndex].Y = -Math.Abs(ballSpeed[ballIndex].Y);
                    }

                    if (ballCoords[ballIndex].Y < minBallY + (ballCoords[ballIndex].Z / maxBallZ))
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

                client.SetLedsColours(c => (RGBValue)Enumerable.Range(0, ballCoords.Length)
                                                     .Select(b => Vector3.Distance(c, ballCoords[b]) < ballSize ?
                                                                  ballColour[b] * (1 - (Vector3.Distance(c, ballCoords[b]) / ballSize)) :
                                                                  Colours.Black)
                                                     .Aggregate((l, r) => l + r));

                await ApplyUpdate(client, cancellationToken, delayAfterMS: 50);
            }
        }
    }
}
