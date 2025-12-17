using Microsoft.VisualBasic.FileIO;
using StrangerThingsLights.Models;
using System.Numerics;
using WLEDInterface;

namespace StrangerThingsLights.BackgroundTaskManagement
{
    public partial class WledPatterns
    {
        public async ValueTask Words(WledClient client, LightsLayoutModel lightsLayoutModel, string wordToDisplay, CancellationToken cancellationToken)
        {
            wordToDisplay = wordToDisplay.ToLowerInvariant();
            client.SetAllLeds(Colours.Black);
            await ApplyUpdate(client, cancellationToken);
            
            var lightIndex = 0;
            var previousLightIndex = 0;
            var charIndex = 0;
            var speedOfLightsMS = (int)TimeSpan.FromSeconds(2).TotalMilliseconds;

            while (!cancellationToken.IsCancellationRequested)
            {
                // Fade out the previous letter
                await FadeLight(client, previousLightIndex, speedOfLightsMS, Colours.Black, cancellationToken);

                if (charIndex >= wordToDisplay.Length) break;

                if (wordToDisplay[charIndex] == ' ')
                {
                    // for a space just wait half the time for one light
                    await Task.Delay(speedOfLightsMS / 3, cancellationToken);
                }
                else if (wordToDisplay[charIndex] < 'a' || wordToDisplay[charIndex] > 'z')
                {
                    // Ignore unsupported characters
                    charIndex++;
                    continue;
                }
                else
                {
                    lightIndex = lightsLayoutModel.GetLetterLightIndex(wordToDisplay[charIndex]);

                    // Fade in the next letter
                    await FadeLight(client, lightIndex, speedOfLightsMS, Colours.Red, cancellationToken);
                }

                charIndex++;
                previousLightIndex = lightIndex;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                // Fade out the last letter
                client.SetLedColour(previousLightIndex, Colours.Black);
                await ApplyUpdate(client, cancellationToken, delayAfterMS: 1000);

                // Wait a bit then restore the state
                await Task.Delay(5000, cancellationToken);

                await client.RestoreState();
            }
        }

        private async Task FadeLight(WledClient client, int lightIndex, int speedOfLightsMS, RGBValue to, CancellationToken cancellationToken)
        {
            var from = client.GetLedColour(lightIndex);
            // Fade out the previous letter
            var startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var endTime = startTime + speedOfLightsMS;

            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (now >= endTime)
                {
                    break;
                }

                var progress = (now - startTime) / (double)(endTime - startTime);
                var colour = ((to - from) * progress) + from;
                client.SetLedColour(lightIndex, (RGBValue)colour);
                await ApplyUpdate(client, cancellationToken);
            }
            // Make sure it's fully set to the target colour
            client.SetLedColour(lightIndex, to);
            await ApplyUpdate(client, cancellationToken);
        }
    }
}
