using Newtonsoft.Json.Linq;
using System.Numerics;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        public async ValueTask Contagion(WledTreeClient client, CancellationToken cancellationToken)
        {
            Console.WriteLine("Set all to black");
            client.SetAllLeds(Colours.Black);
            await ApplyUpdate(client, cancellationToken);

            // Compute the distance between all LEDs
            var distances = new Dictionary<int, Dictionary<int, float>>();
            for (var i = client.LedIndexStart; i < client.LedIndexEnd; i++)
            {
                distances[i] = new Dictionary<int, float>();
                for (var j = client.LedIndexStart; j < client.LedIndexEnd; j++)
                {
                    // We do it both ways round to sacrifice memory for speed
                    distances[i][j] = Vector3.Distance(client.LedCoordinates[i], client.LedCoordinates[j]);
                }
            }

            var usePrimaryColour = true;
            var litIndexes = new HashSet<int>();
            var unlitIndexes = new HashSet<int>(Enumerable.Range(client.LedIndexStart, client.LedIndexEnd - client.LedIndexStart));
            var rand = new Random();
            while (!cancellationToken.IsCancellationRequested)
            {

                // First choose a random LED to start the contagion
                var nextLedIndex = new Random().Next(client.LedIndexStart, client.LedIndexEnd);

                client.SetLedColour(nextLedIndex, usePrimaryColour ? Colours.Red : Colours.Green);
                await ApplyUpdate(client, cancellationToken, delayAfterMS: 50);

                litIndexes.Add(nextLedIndex);
                unlitIndexes.Remove(nextLedIndex);

                while (!cancellationToken.IsCancellationRequested && unlitIndexes.Count != 0)
                {
                    if (cancellationToken.IsCancellationRequested) { break; }

                    // Find the closest unlit LED to the current lit LEDs (randomly choose from the top 5)
                    var closestUnlitLedIndex = unlitIndexes.OrderBy(unlitIndex => litIndexes.Min(litIndex => distances[litIndex][unlitIndex])).Take(5).RandomElement(rand);

                    await client.FadeLight(closestUnlitLedIndex, 250, usePrimaryColour ? Colours.Red : Colours.Green, cancellationToken);
                    litIndexes.Add(closestUnlitLedIndex);
                    unlitIndexes.Remove(closestUnlitLedIndex);

                    await ApplyUpdate(client, cancellationToken, delayAfterMS: 50);
                }
                usePrimaryColour = !usePrimaryColour;
                // Swap the hashsets around
                (unlitIndexes, litIndexes) = (litIndexes, unlitIndexes);
            }
        }
    }
}