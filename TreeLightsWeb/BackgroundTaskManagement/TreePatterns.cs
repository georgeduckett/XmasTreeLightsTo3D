using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        private async Task DelayNoException(int delay, CancellationToken ct)
        {
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException) { }
        }
        private async Task ApplyUpdate(WledTreeClient client, CancellationToken ct, int delayBeforeMS = 0, int delayAfterMS = 0)
        {
            var ledUpdates = await client.ApplyUpdate(ct, delayBeforeMS, delayAfterMS);

            
        }
    }
}
