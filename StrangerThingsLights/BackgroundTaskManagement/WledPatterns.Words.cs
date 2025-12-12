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

            while (!cancellationToken.IsCancellationRequested && charIndex < wordToDisplay.Length)
            {
                // TODO: Fade between letters instead of just switching
                if (wordToDisplay[charIndex] == ' ')
                {
                    client.SetLedColour(previousLightIndex, Colours.Black);
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
                    client.SetLedsColours(new WledClient.LedUpdate(previousLightIndex, Colours.Black), new WledClient.LedUpdate(lightIndex, Colours.Red));
                }


                await ApplyUpdate(client, cancellationToken, delayAfterMS: 1000);

                charIndex++;
                previousLightIndex = lightIndex;
            }

            // TODO: Fade out the last letter
            client.SetLedColour(previousLightIndex, Colours.Black);
            await ApplyUpdate(client, cancellationToken, delayAfterMS: 1000);

            // Wait a bit then restore the state
            await Task.Delay(5000, cancellationToken);

            await client.RestoreState();
        }
    }
}
