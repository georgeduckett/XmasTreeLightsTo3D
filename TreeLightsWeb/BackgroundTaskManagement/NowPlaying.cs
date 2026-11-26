using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    /// <summary>What's playing on the tree (Name is null if nothing is), and how long it's been playing.</summary>
    public sealed record NowPlayingState(string? Name, string? Label, double ElapsedSeconds, ShowcaseState? Showcase)
    {
        public static NowPlayingState Nothing { get; } = new(null, null, 0, null);
    }

    /// <summary>The effect the Showcase is showing, how long it's been showing out of how long it'll show, and the next.</summary>
    public sealed record ShowcaseState(string Name, string Label, double ElapsedSeconds, double Seconds, string? NextName, string? NextLabel);

    /// <summary>Sends <see cref="NowPlayingState"/> to the home page ("NowPlaying") when it connects and whenever it changes.</summary>
    public sealed class NowPlayingHub(NowPlaying nowPlaying) : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("NowPlaying", nowPlaying.State);
            await base.OnConnectedAsync();
        }
    }

    /// <summary>
    /// Keeps track of what's playing on the tree: animations are started through <see cref="Play"/>, which notes when each
    /// starts and finishes (it finishes when it's stopped or another starts), and the Showcase says which effect it's on.
    /// Anything that changes is sent to the home page.
    /// </summary>
    public sealed class NowPlaying(IHubContext<NowPlayingHub> hub, IWledClientTaskManager<WledTreeClient> taskManager, ILogger<NowPlaying> logger)
    {
        private readonly object _lock = new();
        private readonly SemaphoreSlim _sending = new(1, 1);
        private int _run;
        private string? _name, _label;
        private long _started;
        private (string Name, string Label, long Started, double Seconds, string? NextName, string? NextLabel)? _showcase;

        public NowPlayingState State
        {
            get
            {
                lock (_lock)
                {
                    if (_name == null) return NowPlayingState.Nothing;
                    var now = Stopwatch.GetTimestamp();
                    var showcase = _showcase is { } s
                        ? new ShowcaseState(s.Name, s.Label, Stopwatch.GetElapsedTime(s.Started, now).TotalSeconds, s.Seconds, s.NextName, s.NextLabel)
                        : null;
                    return new NowPlayingState(_name, _label, Stopwatch.GetElapsedTime(_started, now).TotalSeconds, showcase);
                }
            }
        }

        /// <summary>
        /// Starts an animation (stopping whatever's playing), and returns once it's started (or after a couple of seconds,
        /// if it's slow to), with what's playing then.
        /// </summary>
        /// <param name="name">Its name (as TreePatterns has it, e.g. "Snake").</param>
        /// <param name="label">What it's called on the home page, e.g. "Rotate Around Axis".</param>
        public async Task<NowPlayingState> Play(string name, string label, Func<WledTreeClient, CancellationToken, ValueTask> animation)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await taskManager.QueueAnimation(async (client, cancellationToken) =>
            {
                var run = Started(name, label);
                started.TrySetResult();
                try
                {
                    await animation(client, cancellationToken);
                }
                finally
                {
                    Finished(run);
                }
            });
            await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            return State;
        }

        /// <summary>Stops whatever's playing, and returns once it has (or after a couple of seconds).</summary>
        public async Task<NowPlayingState> Stop()
        {
            int run;
            lock (_lock) run = _run;
            await taskManager.StopRunningTask();
            for (var waited = 0; waited < 2000 && IsPlaying(run); waited += 50)
            {
                await Task.Delay(50);
            }
            return State;
        }

        /// <summary>The Showcase is now showing the effect <paramref name="name"/>, for <paramref name="seconds"/>.</summary>
        public void ShowcaseShowing(string name, string label, double seconds, string? nextName, string? nextLabel)
        {
            lock (_lock)
            {
                if (_name != nameof(TreePatterns.Showcase)) return;
                _showcase = (name, label, Stopwatch.GetTimestamp(), seconds, nextName, nextLabel);
            }
            Send();
        }

        private bool IsPlaying(int run)
        {
            lock (_lock) return _run == run && _name != null;
        }

        private int Started(string name, string label)
        {
            int run;
            lock (_lock)
            {
                run = ++_run;
                (_name, _label, _started, _showcase) = (name, label, Stopwatch.GetTimestamp(), null);
            }
            Send();
            return run;
        }

        private void Finished(int run)
        {
            lock (_lock)
            {
                // (If another's started since, it's that that's playing)
                if (run != _run) return;
                (_name, _label, _showcase) = (null, null, null);
            }
            Send();
        }

        private void Send()
        {
            // Sent in the background, so an animation never waits for it, one at a time, and always what's playing at the
            // time it's sent (so if two changes are close together, the last one sent is the latest)
            _ = Task.Run(async () =>
            {
                await _sending.WaitAsync();
                try
                {
                    await hub.Clients.All.SendAsync("NowPlaying", State);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Couldn't send what's playing to the home page.");
                }
                finally
                {
                    _sending.Release();
                }
            });
        }
    }
}
