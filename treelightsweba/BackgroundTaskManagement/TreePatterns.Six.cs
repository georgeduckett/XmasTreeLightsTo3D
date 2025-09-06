using System.Diagnostics;
using System.Numerics;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        public async ValueTask Six(WledTreeClient client, CancellationToken cancellationToken)
        {
            var animations = new List<(long, Func<TreePatterns, WledTreeClient, CancellationToken, ValueTask>)>
            {
                new(0, async (tp, client, cancellationToken) =>
                {
                    client.SetAllLeds(Colours.Red);
                    await tp.ApplyUpdate(client, cancellationToken);
                }),
                new(2000, async (tp, client, cancellationToken) =>
                {
                    client.SetAllLeds(Colours.Green);
                    await tp.ApplyUpdate(client, cancellationToken);
                }),
                new(5000, async (tp, client, cancellationToken) =>
                {
                    client.SetAllLeds(Colours.Blue);
                    await tp.ApplyUpdate(client, cancellationToken);
                }),
                new(10000, async (tp, client, cancellationToken) =>
                {
                    client.SetAllLeds(Colours.Red);
                    await tp.ApplyUpdate(client, cancellationToken);
                }),
                new(11000, async (tp, client, cancellationToken) =>
                {
                    client.SetAllLeds(Colours.Green);
                    await tp.ApplyUpdate(client, cancellationToken);
                }),
            };


            var stopwatch = Stopwatch.StartNew();

            for (var i = 0; i < animations.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) { break; }
                var (delay, animation) = animations[i];

                var waitDuration = delay - stopwatch.ElapsedMilliseconds;
                if (waitDuration > 0)
                {
                    await DelayNoException((int)(delay - stopwatch.ElapsedMilliseconds), cancellationToken);
                }
                else if (i > 1)
                {
                    throw new InvalidOperationException($"We passed when animation {i} should have ran.");
                }
                await animation(this, client, cancellationToken);
            }
        }
    }
}
