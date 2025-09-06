using System.Numerics;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        public async ValueTask Binary(WledTreeClient client, CancellationToken cancellationToken)
        {
            Console.WriteLine("Flashing indexes in binary");
            // Convert all numbers to binary
            var binary = Enumerable.Range(client.LedIndexStart, client.LedIndexEnd - client.LedIndexStart)
                .Select(i => new string(Convert.ToString(i, 2).Reverse().ToArray())).ToArray();
            while (!cancellationToken.IsCancellationRequested)
            {
                client.SetAllLeds(Colours.Black);
                await ApplyUpdate(client, cancellationToken, delayBeforeMS: 4000);

                for (var bi = 0; bi < binary.Max(bString => bString.Length); bi++)
                {
                    if (cancellationToken.IsCancellationRequested) { break; }

                    client.SetLedsColours((i, c) => bi >= binary[i].Length || binary[i][bi] == '0' ? Colours.Red : Colours.White);
                    await ApplyUpdate(client, cancellationToken, delayAfterMS: 1000);
                }
            }
        }
    }
}
