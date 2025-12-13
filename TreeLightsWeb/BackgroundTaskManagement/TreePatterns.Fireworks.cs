using System.Numerics;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        public async ValueTask Fireworks(WledTreeClient client, CancellationToken cancellationToken)
        {
            Console.WriteLine("Fireworks");
            var rand = new Random();

            client.SetAllLeds(Colours.Black);
            await ApplyUpdate(client, cancellationToken, delayBeforeMS: 4000);

            while (!cancellationToken.IsCancellationRequested)
            {
                // Choose a firework launch position
                var fireworkPosition = new Vector3(rand.NextSingle() * 2 - 1, rand.NextSingle() * 2 - 1, 0f);

                // Decide on the firework direction, towards the top of the tree towards the centre
                var fireworkDirection = new Vector3(-fireworkPosition.X * 0.01f, -fireworkPosition.Y * 0.01f, rand.SingleBetween(0.3f, 0.38f));

                int closestLEDIndex = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Clear the previous firework LED
                    client.SetLedColour(closestLEDIndex, Colours.Black);

                    // Move the firework position along its direction
                    fireworkPosition += fireworkDirection;
                    // Apply a slight gravity effect
                    fireworkDirection += new Vector3(0f, 0f, -0.02f);

                    // If we start going downwards a significant amount, end the firework
                    if (fireworkDirection.Z < -0.1f)
                    {
                        break;
                    }

                    // Find the closest LED to the firework position
                    closestLEDIndex = client.LedCoordinates
                        .Select((coord, index) => new { coord, index })
                        .OrderBy(c => Vector3.Distance(c.coord, fireworkPosition))
                        .First().index;

                    // Set the firework LED to white
                    client.SetLedColour(closestLEDIndex, Colours.White);

                    await ApplyUpdate(client, cancellationToken, delayAfterMS: 250);
                }

                fireworkDirection = new Vector3(0f, 0f, 0f);
                // Explosion
                var explosionRadius = 0.1f;
                for (var step = 0; step < 10; step++)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var currentRadius = explosionRadius * (step + 1);
                    client.SetLedsColours(c =>
                    {
                        var distance = Vector3.Distance(c, fireworkPosition);
                        if (distance > currentRadius)
                        {
                            return Colours.Black;
                        }
                        else if (distance > currentRadius * 0.8)
                        {
                            return (RGBValue)(Colours.Red * (distance / currentRadius));
                        }
                        else
                        {
                            return Colours.Black;
                        }
                    });

                    await ApplyUpdate(client, cancellationToken, delayAfterMS: 100);

                    fireworkPosition += fireworkDirection;
                    fireworkDirection += new Vector3(0f, 0f, -0.01f);

                    client.SetAllLeds(Colours.Black);
                    await ApplyUpdate(client, cancellationToken, delayAfterMS: 100);
                }
            }
        }
    }
}
