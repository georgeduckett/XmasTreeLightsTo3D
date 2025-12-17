using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WLEDInterface
{
    public interface IWledClientTaskManager<TClient> where TClient : WledClient
    {
        ValueTask QueueAnimation(Func<TClient, CancellationToken, ValueTask> workItem);

        Task<Func<ValueTask>> DequeueAsync(TClient client, CancellationToken cancellationToken);
        Task StopRunningTask();
        Task RestoreState();
        Task Reboot();
        Task Reconnect();
        Task TurnOff();
    }

    public class WledClientTaskManager<TClient> : IWledClientTaskManager<TClient>, IDisposable where TClient : WledClient
    {
        // This doesn't really need to be a queue, as we only have one item
        private readonly Channel<Func<TClient, CancellationToken, ValueTask>> _queue;
        private CancellationTokenSource _CurrentTaskCancellationToken;

        public WledClientTaskManager()
        {
            // Capacity should be set based on the expected application load and
            // number of concurrent threads accessing the queue.
            // BoundedChannelFullMode.Wait will cause calls to WriteAsync() to return a task,
            // which completes only when space became available. This leads to backpressure,
            // in case too many publishers/calls start accumulating.
            var options = new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            };
            _queue = Channel.CreateBounded<Func<TClient, CancellationToken, ValueTask>>(options);
            _CurrentTaskCancellationToken = new CancellationTokenSource();
        }

        public async ValueTask QueueAnimation(
            Func<TClient, CancellationToken, ValueTask> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);

            // We're ready for the next task
            await StopRunningTask();
            await _queue.Writer.WriteAsync(workItem);
        }

        public async Task<Func<ValueTask>> DequeueAsync(TClient client, CancellationToken cancellationToken)
        {
            // Wait until there's something to read and only then set a new cancellation token
            await _queue.Reader.WaitToReadAsync(cancellationToken);

            _CurrentTaskCancellationToken = new CancellationTokenSource();
            // Return a task that calls the task to run, with the next task ready cancellation token (so we can cancel it from here)
            return async () =>
            {
                var taskToRun = await _queue.Reader.ReadAsync(cancellationToken);
                await taskToRun(client, _CurrentTaskCancellationToken.Token);
            };
        }
        public async Task StopRunningTask()
        {
            await _CurrentTaskCancellationToken.CancelAsync();
        }

        public async Task RestoreState() => await QueueAnimation(async (client, ct) => await client.RestoreState());

        public async Task Reboot() => await QueueAnimation(async (client, ct) => await client.Reboot());
        public async Task TurnOff() => await QueueAnimation(async (client, ct) => await client.SetOnOff(false));

        public void Dispose()
        {
            _CurrentTaskCancellationToken.Cancel();
            _CurrentTaskCancellationToken.Dispose();

            GC.SuppressFinalize(this);
        }

        public async Task Reconnect() => await QueueAnimation(async (client, ct) => { await client.LoadStateAsync(); });
    }
}
