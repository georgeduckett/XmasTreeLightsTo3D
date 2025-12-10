using System.Numerics;
using WLEDInterface;

namespace StrangerThingsLights.BackgroundTaskManagement
{
    public partial class WledPatterns
    {
        public async ValueTask Words(WledClient client, CancellationToken cancellationToken)
        {
            client.SetAllLeds(Colours.Black);
            await ApplyUpdate(client, cancellationToken);
            
            while (!cancellationToken.IsCancellationRequested)
            {
                //client.SetLedsColours

                await ApplyUpdate(client, cancellationToken, delayAfterMS: 50);
            }
        }
    }
}
