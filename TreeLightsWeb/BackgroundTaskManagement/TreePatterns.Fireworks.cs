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
            await ApplyUpdate(client, cancellationToken, delayBeforeMS: Math.Min(4000, ScaleDelay(4000, _settings.SpeedFor(nameof(Fireworks)))));

            var treeHeight = client.LedCoordinates.Max(c => c.Z);
            const float gravity = 0.02f; // per frame
            var fireworkNumber = 0;
            var colours = _settings.ColoursFor(FireworksColours);

            while (!cancellationToken.IsCancellationRequested)
            {
                // Choose a firework launch position, near the trunk (anywhere in -1..1 would often be outside the tree)
                var launchAngle = rand.NextSingle() * MathF.Tau;
                var launchRadius = MathF.Sqrt(rand.NextSingle()) * 0.5f;
                var fireworkPosition = new Vector3(launchRadius * MathF.Cos(launchAngle), launchRadius * MathF.Sin(launchAngle), 0f);

                // Decide on the firework direction, upwards and towards the centre, fast enough to burst 45-85% of the way up
                // the tree. It bursts once it's falling at 0.1 a frame, 0.1²/(2g) = 0.25 below the top of its climb.
                var burstHeight = rand.SingleBetween(0.45f, 0.85f) * treeHeight;
                var launchSpeed = MathF.Sqrt(2 * gravity * (burstHeight + 0.25f));
                var fireworkDirection = new Vector3(-fireworkPosition.X * 0.01f, -fireworkPosition.Y * 0.01f, launchSpeed);

                int closestLEDIndex = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Clear the previous firework LED
                    client.SetLedColour(closestLEDIndex, Colours.Black);

                    // Move the firework position along its direction
                    fireworkPosition += fireworkDirection;
                    // Apply a slight gravity effect
                    fireworkDirection += new Vector3(0f, 0f, -gravity);

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

                    // Set the firework LED to the rocket's colour (white, unless it's been given its own colours)
                    client.SetLedColour(closestLEDIndex, colours["Rocket"].ToRGBValue());

                    await ApplyUpdate(client, cancellationToken, delayAfterMS: ScaleDelay(250, _settings.SpeedFor(nameof(Fireworks))));
                }

                fireworkDirection = new Vector3(0f, 0f, 0f);
                // Each firework is the next of the palette's colours (or, if it's been given its own colours, its burst colour)
                var shellColour = (colours.Own("Bursts") ?? _settings.Palette[fireworkNumber]).ToRGBValue();
                fireworkNumber++;
                // Explosion: a shell that grows to a radius of 1 over 2 seconds, falling slightly and fading as it goes (in
                // smaller steps, with no black frame between them, so it spreads out smoothly rather than flickering)
                const int explosionSteps = 20;
                var explosionRadius = 1f / explosionSteps;
                for (var step = 0; step < explosionSteps; step++)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var currentRadius = explosionRadius * (step + 1);
                    var fade = 1 - 0.6f * step / explosionSteps;
                    client.SetLedsColours(c =>
                    {
                        var distance = Vector3.Distance(c, fireworkPosition);
                        if (distance > currentRadius)
                        {
                            return Colours.Black;
                        }
                        else if (distance > currentRadius * 0.8)
                        {
                            return (RGBValue)(shellColour * (fade * distance / currentRadius));
                        }
                        else
                        {
                            return Colours.Black;
                        }
                    });

                    await ApplyUpdate(client, cancellationToken, delayAfterMS: ScaleDelay(100, _settings.SpeedFor(nameof(Fireworks))));

                    fireworkPosition += fireworkDirection;
                    fireworkDirection += new Vector3(0f, 0f, -0.005f);
                }

                client.SetAllLeds(Colours.Black);
                await ApplyUpdate(client, cancellationToken, delayAfterMS: ScaleDelay(100, _settings.SpeedFor(nameof(Fireworks))));
            }
        }
    }
}
