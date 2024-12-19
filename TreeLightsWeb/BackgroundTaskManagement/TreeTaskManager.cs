﻿using Microsoft.Extensions.Hosting.Internal;
using System.Threading;
using System.Threading.Channels;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public interface ITreeTaskManager
    {
        ValueTask QueueTreeAnimation(Func<WledTreeClient, CancellationToken, ValueTask> workItem);

        Task<Func<ValueTask>> DequeueAsync(WledTreeClient client, CancellationToken cancellationToken);
        Task StopRunningTask();
        Task RestoreTreeState();
        Task RebootTree();
    }

    public class TreeTaskManager : ITreeTaskManager, IDisposable
    {
        // This doesn't really need to be a queue, as we only have one item
        private readonly Channel<Func<WledTreeClient, CancellationToken, ValueTask>> _queue;
        private CancellationTokenSource _CurrentTaskCancellationToken;

        public TreeTaskManager()
        {
            // Capacity should be set based on the expected application load and
            // number of concurrent threads accessing the queue.
            // BoundedChannelFullMode.Wait will cause calls to WriteAsync() to return a task,
            // which completes only when space became available. This leads to backpressure,
            // in case too many publishers/calls start accumulating.
            var options = new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.Wait, SingleReader = true,
            };
            _queue = Channel.CreateBounded<Func<WledTreeClient, CancellationToken, ValueTask>>(options);
            _CurrentTaskCancellationToken = new CancellationTokenSource();
        }

        public async ValueTask QueueTreeAnimation(
            Func<WledTreeClient, CancellationToken, ValueTask> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);

            // We're ready for the next task
            _CurrentTaskCancellationToken.Cancel();
            await _queue.Writer.WriteAsync(workItem);
        }

        public async Task<Func<ValueTask>> DequeueAsync(WledTreeClient client, CancellationToken cancellationToken)
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

        public async Task RestoreTreeState() => await QueueTreeAnimation(async (client, ct) => await client.RestoreState());

        public async Task RebootTree() => await QueueTreeAnimation(async (client, ct) => await client.Reboot());

        public void Dispose()
        {
            _CurrentTaskCancellationToken.Cancel();
            _CurrentTaskCancellationToken.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
