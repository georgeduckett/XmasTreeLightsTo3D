using Microsoft.AspNetCore.SignalR;
using WLEDInterface;

namespace StrangerThingsLights.BackgroundTaskManagement
{
    public partial class WledPatterns
    {
        public WledPatterns()
        {

        }
        private async Task DelayNoException(int delay, CancellationToken ct)
        {
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException) { }
        }
        private async Task ApplyUpdate(WledClient client, CancellationToken ct, int delayBeforeMS = 0, int delayAfterMS = 0)
        {
            var ledUpdates = await client.ApplyUpdate(ct, delayBeforeMS, delayAfterMS);

            // TODO: Update a SignalR hub to notify clients of the LED updates
        }
    }
}
