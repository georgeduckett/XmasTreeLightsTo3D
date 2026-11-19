using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using WLEDInterface;

namespace TreeLightsWeb.ImageCapture
{
    /// <summary>
    /// Runs image captures (and camera latency measurements) as background jobs, so they don't depend on any one HTTP request
    /// or SignalR connection staying alive.
    ///
    /// For each image, the job lights the tree with a single self-contained WLED request ("everything black except LED n"),
    /// asks the capture page (which owns the camera) for a frame taken after the settle delay, and moves on as soon as the
    /// page has grabbed it. The page encodes and uploads the PNG in the background while the next LED is lit, so encoding,
    /// transfer and saving are off the critical path. Jobs run through the WLED task queue, so nothing else drives the LEDs
    /// meanwhile (starting a light pattern interrupts the capture cleanly instead of corrupting it).
    ///
    /// It copes with WLED not responding or being power-cycled (keeps retrying, re-sending the full LED state), the camera
    /// page reconnecting or being reloaded (waits for it, then retries the frame; anything that didn't upload is
    /// re-captured), and stopping part way (images go to a staging folder, so the previous set is untouched until a capture
    /// completes, and an incomplete capture can be resumed).
    /// </summary>
    public sealed class ImageCaptureCoordinator
    {
        private const int TreeRequestTimeoutMs = 5000;
        private const int FrameTimeoutMs = 15000; // allowed on top of the settle delay for the page to return a frame
        private const int MaxOutstandingUploads = 12; // grabbed frames the page hasn't finished uploading
        private static readonly TimeSpan UploadStallTimeout = TimeSpan.FromSeconds(20);
        private const int MaxRecaptureRounds = 3;
        private const int LatencyTrials = 6;
        private const int LatencyWindowMs = 1500;

        private readonly IWledClientTaskManager<WledTreeClient> _taskManager;
        private readonly WledTreeClient _treeClient;
        private readonly IHubContext<ImageCaptureHub> _hub;
        private readonly CapturedImageStore _store;
        private readonly ILogger<ImageCaptureCoordinator> _logger;
        private readonly CancellationToken _appStopping;

        private readonly SemaphoreSlim _queueGate = new(1, 1); // serialises "check nothing's running, then queue work on the tree"
        private readonly object _sync = new();
        private readonly Dictionary<string, string> _cameraConnections = []; // capture page client id -> current connection id
        private readonly Dictionary<string, string> _connectionClients = []; // connection id -> capture page client id
        private readonly Dictionary<string, CancellationTokenSource> _connectionLifetimes = []; // cancelled once a connection is superseded or closed
        private readonly Dictionary<string, string> _cameraPages = []; // capture page client id -> id of the page load
        private TaskCompletionSource _camerasChanged = NewSignal();
        private Job? _job;
        private CaptureStatus _status = CaptureStatus.Idle;
        private long _statusVersion;

        public ImageCaptureCoordinator(IWledClientTaskManager<WledTreeClient> taskManager, WledTreeClient treeClient,
            IHubContext<ImageCaptureHub> hub, CapturedImageStore store, IHostApplicationLifetime lifetime,
            ILogger<ImageCaptureCoordinator> logger)
        {
            _taskManager = taskManager;
            _treeClient = treeClient;
            _hub = hub;
            _store = store;
            _logger = logger;
            _appStopping = lifetime.ApplicationStopping;
        }

        public CaptureStatus Status
        {
            get { lock (_sync) return _status; }
        }

        public bool IsBusy
        {
            get { lock (_sync) return _job is { Finished: false }; }
        }

        #region Capture pages (cameras)

        /// <summary>
        /// Called when a capture page connects or reconnects. The client id is stable for the browser tab (even across page
        /// reloads), so a capture carries on with the same page after a reconnection.
        /// </summary>
        public CaptureStatus RegisterCamera(string clientId, string pageId, string connectionId)
        {
            CancellationTokenSource? superseded = null;
            CaptureStatus status;
            lock (_sync)
            {
                if (_cameraPages.TryGetValue(clientId, out var previousPage) && previousPage != pageId &&
                    _job is { Finished: false } job && job.ClientId == clientId)
                { // The page has been reloaded, so any images it hadn't finished uploading have gone: take them again
                    lock (job.Lock)
                    {
                        job.Outstanding.Clear();
                        SignalUploads(job);
                    }
                }
                _cameraPages[clientId] = pageId;

                if (_cameraConnections.TryGetValue(clientId, out var previous) && previous != connectionId)
                { // The page has reconnected (or been reloaded), so anything we asked of its old connection won't be answered
                    _connectionClients.Remove(previous);
                    _connectionLifetimes.Remove(previous, out superseded);
                }
                _cameraConnections[clientId] = connectionId;
                _connectionClients[connectionId] = clientId;
                if (!_connectionLifetimes.ContainsKey(connectionId))
                {
                    _connectionLifetimes[connectionId] = new CancellationTokenSource();
                }
                SignalCamerasChanged();
                status = _status;
            }
            superseded?.Cancel(); // outside the lock, as it runs the cancellation callbacks
            return status;
        }

        /// <summary>
        /// Called by the capture page when it's given up uploading an image, so it's re-captured without waiting.
        /// </summary>
        public void UploadFailed(string jobId, string name, long seq)
        {
            Job? job;
            lock (_sync) job = _job;
            if (job == null || job.Id != jobId)
            {
                return;
            }
            lock (job.Lock)
            {
                if (job.AcceptedSeq.TryGetValue(name, out var accepted) && accepted == seq && !job.Saved.Contains(name))
                {
                    job.Outstanding.Remove(name);
                    SignalUploads(job);
                }
            }
        }

        public void ConnectionClosed(string connectionId)
        {
            CancellationTokenSource? closed;
            lock (_sync)
            {
                if (_connectionClients.Remove(connectionId, out var clientId) &&
                    _cameraConnections.TryGetValue(clientId, out var current) && current == connectionId)
                {
                    _cameraConnections.Remove(clientId);
                }
                _connectionLifetimes.Remove(connectionId, out closed);
                SignalCamerasChanged();
            }
            closed?.Cancel(); // outside the lock, as it runs the cancellation callbacks
        }

        private bool IsCameraConnected(string clientId)
        {
            lock (_sync) return _cameraConnections.ContainsKey(clientId);
        }

        private void SignalCamerasChanged()
        {
            var signal = _camerasChanged;
            _camerasChanged = NewSignal();
            signal.TrySetResult();
        }

        /// <summary>
        /// Waits until the job's capture page is connected. Returns its connection, and a token that's cancelled when that
        /// connection is closed or replaced by a newer one (e.g. the page reconnected), so requests to it can be abandoned.
        /// </summary>
        private async Task<(string ConnectionId, CancellationToken Lifetime)> WaitForCameraAsync(Job job, CancellationToken ct)
        {
            var announced = false;
            while (true)
            {
                Task changed;
                lock (_sync)
                {
                    if (_cameraConnections.TryGetValue(job.ClientId, out var connectionId) &&
                        _connectionLifetimes.TryGetValue(connectionId, out var lifetime))
                    {
                        return (connectionId, lifetime.Token);
                    }
                    changed = _camerasChanged.Task;
                }

                if (!announced)
                {
                    Publish(job, CaptureState.Running, "The capture page has disconnected; waiting for it to reconnect. " +
                        "(If it was closed, reopen the capture page, press Stop, then Resume.)", paused: true);
                    announced = true;
                }
                await Task.WhenAny(changed, Task.Delay(5000, ct));
                ct.ThrowIfCancellationRequested();
            }
        }

        #endregion

        #region Starting and stopping

        public async Task<StartResult> StartCaptureAsync(StartCaptureRequest request)
        {
            if (!CaptureDirections.IsValid(request.Direction))
                return Invalid($"The direction must be one of {string.Join(", ", CaptureDirections.All)}.");
            if (request.SettleDelayMs is < 0 or > 10_000)
                return Invalid("The settle delay must be between 0 and 10000 ms.");
            if (request.ExposureSettleMs is < 0 or > 30_000)
                return Invalid("The exposure settle time must be between 0 and 30000 ms.");
            if (request.Width <= 0 || request.Height <= 0)
                return Invalid("The camera isn't ready yet.");
            if (string.IsNullOrEmpty(request.ClientId) || !IsCameraConnected(request.ClientId))
                return Invalid("This page isn't connected to the server yet; wait a moment and try again.");

            if (!request.AllowResolutionMismatch)
            {
                var others = _store.GetResolutionsOfOtherDirections(request.Direction);
                if (others.Any(size => size != (request.Width, request.Height)))
                {
                    var existing = string.Join(" and ", others.Select(s => $"{s.Width}×{s.Height}"));
                    return new StartResult(false, Code: "resolution-mismatch",
                        Error: $"Images for other directions were captured at {existing}, but the camera is now {request.Width}×{request.Height}. " +
                               "Image processing needs every direction to be captured at the same resolution.");
                }
            }

            var job = new Job(CaptureJobKind.Capture, request.ClientId)
            {
                Direction = request.Direction,
                SettleDelayMs = request.SettleDelayMs,
                ExposureSettleMs = request.ExposureSettleMs,
                Resume = request.Resume,
                Width = request.Width,
                Height = request.Height,
            };
            return await QueueJobAsync(job, RunCaptureAsync);
        }

        public async Task<StartResult> MeasureLatencyAsync(MeasureLatencyRequest request)
        {
            if (string.IsNullOrEmpty(request.ClientId) || !IsCameraConnected(request.ClientId))
                return Invalid("This page isn't connected to the server yet; wait a moment and try again.");

            return await QueueJobAsync(new Job(CaptureJobKind.Latency, request.ClientId), RunLatencyAsync);
        }

        public void Stop()
        {
            Job? job;
            lock (_sync) job = _job;
            if (job == null || job.Finished)
            {
                return;
            }

            job.StopRequested.Cancel();
            lock (_sync)
            {
                if (job.State == CaptureState.Queued)
                { // Hasn't started yet, and now won't
                    Publish(job, CaptureState.Cancelled, "Stopped.");
                }
            }
        }

        /// <summary>
        /// Lights the whole tree so the camera can be lined up, unless a capture is under way (e.g. its page was reloaded).
        /// </summary>
        public async Task<bool> LightTreeForFramingAsync()
        {
            await _queueGate.WaitAsync();
            try
            {
                if (IsBusy)
                {
                    return false;
                }

                await _taskManager.QueueAnimation(async (client, ct) =>
                {
                    if (!client.IsConnected) return;
                    try
                    {
                        await LightWholeTreeAsync(client, null, ct);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "Couldn't light the tree for framing");
                    }
                });
                return true;
            }
            finally
            {
                _queueGate.Release();
            }
        }

        public CaptureSummary GetSummary()
        {
            var connected = _treeClient.IsConnected;
            return connected
                ? _store.GetSummary(true, _treeClient.LedIndexStart, _treeClient.LedIndexEnd - _treeClient.LedIndexStart)
                : _store.GetSummary(false, null, null);
        }

        private async Task<StartResult> QueueJobAsync(Job job, Func<Job, WledTreeClient, CancellationToken, Task> run)
        {
            await _queueGate.WaitAsync();
            try
            {
                lock (_sync)
                {
                    if (_job is { Finished: false } current)
                    {
                        return new StartResult(false, Code: "busy", Error: current.Kind == CaptureJobKind.Capture
                            ? "A capture is already running."
                            : "A camera latency measurement is already running.");
                    }
                    _job = job;
                }

                Publish(job, CaptureState.Queued, "Waiting for the tree to finish what it's doing…");
                // Queuing it on the tree's task manager stops any animation and gives the job sole use of the LEDs
                await _taskManager.QueueAnimation((client, ct) => new ValueTask(RunJobAsync(job, client, ct, run)));
                return new StartResult(true, JobId: job.Id);
            }
            finally
            {
                _queueGate.Release();
            }
        }

        private async Task RunJobAsync(Job job, WledTreeClient client, CancellationToken treeCt, Func<Job, WledTreeClient, CancellationToken, Task> run)
        {
            if (job.Finished)
            { // Stopped before it got going
                return;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(treeCt, job.StopRequested.Token, _appStopping);
            var ct = linked.Token;
            var restoreTree = true;
            job.Clock.Start();
            try
            {
                Publish(job, CaptureState.Running, "Starting…");
                await run(job, client, ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            { // Stopped, superseded or shutting down (whatever the exception: e.g. SignalR reports a cancelled request as a HubException)
                var isCapture = job.Kind == CaptureJobKind.Capture;
                if (job.StopRequested.IsCancellationRequested)
                {
                    Publish(job, CaptureState.Cancelled, isCapture ? "Capture stopped. The images taken so far are kept, so you can resume it." : "Measurement stopped.");
                }
                else if (_appStopping.IsCancellationRequested)
                {
                    Publish(job, CaptureState.Cancelled, "Stopped because the server is shutting down.");
                }
                else
                { // Something else has been queued on the tree, and now owns the LEDs
                    restoreTree = false;
                    Publish(job, CaptureState.Cancelled, isCapture
                        ? "Interrupted because another light pattern was started. The images taken so far are kept, so you can resume it."
                        : "Interrupted because another light pattern was started.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image capture job failed");
                Publish(job, CaptureState.Failed, ex is CaptureFailedException ? ex.Message : $"Failed: {ex.Message}");
            }
            finally
            {
                if (restoreTree)
                {
                    await RestoreTreeAsync(job, client);
                }
            }
        }

        private async Task RestoreTreeAsync(Job job, WledTreeClient client)
        {
            try
            {
                // After measuring latency we're about to capture, so light the whole tree for framing. After a capture, put
                // WLED back how it was (as before).
                var restore = job.Kind == CaptureJobKind.Latency
                    ? LightWholeTreeAsync(client, job.Brightness, CancellationToken.None)
                    : client.RestoreState().AsTask();
                if (await Task.WhenAny(restore, Task.Delay(TreeRequestTimeoutMs)) == restore)
                {
                    await restore;
                }
                else
                {
                    _logger.LogWarning("Timed out putting the tree back after image capture");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Couldn't put the tree back after image capture");
            }
        }

        #endregion

        #region Capture

        private async Task RunCaptureAsync(Job job, WledTreeClient client, CancellationToken ct)
        {
            await EnsureTreeConnectedAsync(job, client);
            var ledStart = client.LedIndexStart;
            var ledEnd = client.LedIndexEnd;
            job.Brightness = await ReadBrightnessAsync(job, client, ct);

            // Same order as before: everything on, everything off, then each LED from the end of the string back to the start
            var shots = new List<Shot> { Shot.AllOn, Shot.AllOff };
            for (var led = ledEnd - 1; led >= ledStart; led--)
            {
                shots.Add(Shot.ForLed(led));
            }

            var alreadyCaptured = _store.PrepareStaging(job.Direction, job.Resume,
                new StagingManifest(ledStart, ledEnd - ledStart, job.Width, job.Height, DateTimeOffset.Now));
            int resumedCount;
            lock (job.Lock)
            {
                foreach (var shot in shots)
                {
                    job.ShotNames.Add(shot.Name);
                    if (alreadyCaptured.Contains(shot.Name))
                    {
                        job.Saved.Add(shot.Name);
                        job.Captured.Add(shot.Name);
                    }
                }
                job.Total = shots.Count;
                job.Done = resumedCount = job.Captured.Count;
            }
            Publish(job, CaptureState.Running, resumedCount > 0
                ? $"Resuming: {resumedCount} of {shots.Count} images were already captured."
                : "Capturing…");

            foreach (var shot in shots)
            {
                if (!IsCaptured(job, shot.Name))
                {
                    await CaptureShotAsync(job, client, shot, ct);
                }
            }

            await EnsureEverythingSavedAsync(job, client, shots, ct);

            Publish(job, CaptureState.Running, "Saving the images…");
            lock (job.Lock) job.Committing = true;
            var elapsed = job.Clock.Elapsed;
            _store.Commit(job.Direction, shots.Select(s => s.Name).ToList(), new CaptureManifest(job.Direction, ledStart,
                ledEnd - ledStart, job.Width, job.Height, job.SettleDelayMs, job.ExposureSettleMs, DateTimeOffset.Now, elapsed.TotalSeconds));
            Publish(job, CaptureState.Completed, $"Captured all {shots.Count} images for {job.Direction}° in {FormatDuration(elapsed)}.");
        }

        private async Task CaptureShotAsync(Job job, WledTreeClient client, Shot shot, CancellationToken ct)
        {
            var treeHadProblems = await ShowShotAsync(job, client, shot, ct);
            if (treeHadProblems && job.LastCapturedShot is string previous)
            {
                // The tree stopped responding just after the previous frame was taken. If it was restarted (or lost power)
                // the LEDs may have changed before the camera took that frame, so take it again at the end to be sure.
                ForgetShot(job, previous);
            }

            if (shot.Led == null)
            {
                job.NeedExposureSettle = true;
            }
            else if (job.NeedExposureSettle || treeHadProblems)
            {
                // Going from all the LEDs lit (or whatever WLED shows when it restarts) to one makes the camera's
                // auto-exposure swing a long way, and it takes a moment to settle; without this the first few LED images
                // come out noticeably darker.
                job.NeedExposureSettle = false;
                await SettleExposureAsync(job, ct);
            }

            await GrabFrameAsync(job, client, shot, ct);
            job.LastCapturedShot = shot.Name;
            RecordStep(job);
            Publish(job, CaptureState.Running, "Capturing…");
            await WaitForUploadCapacityAsync(job, ct);
        }

        private async Task SettleExposureAsync(Job job, CancellationToken ct)
        {
            if (job.ExposureSettleMs > 0)
            {
                Publish(job, CaptureState.Running, "Letting the camera's exposure adjust to a single LED…");
                await Task.Delay(job.ExposureSettleMs, ct);
            }
        }

        /// <summary>
        /// Lights the tree for a shot. Returns true if WLED didn't respond first time (so it may have restarted).
        /// </summary>
        private Task<bool> ShowShotAsync(Job job, WledTreeClient client, Shot shot, CancellationToken ct)
        {
            client.SetAllLeds(shot.LightAll ? Colours.White : Colours.Black);
            if (shot.Led is int led)
            {
                client.SetLedColour(led, Colours.White);
            }
            // Always sends the whole state, so the image is right even if the tree missed an earlier update or restarted
            return WithTreeRetriesAsync(job, c => client.ApplyFullStateAsync(c, job.Brightness), ct);
        }

        /// <summary>
        /// Asks the capture page for a frame of the current shot, retrying (after re-sending the LEDs) until it gets one.
        /// </summary>
        private async Task GrabFrameAsync(Job job, WledTreeClient client, Shot shot, CancellationToken ct)
        {
            while (true)
            {
                ThrowIfUploadFailed(job);
                var (connectionId, connectionLifetime) = await WaitForCameraAsync(job, ct);
                var seq = Interlocked.Increment(ref job.LastSeq);
                string problem;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, connectionLifetime);
                    timeout.CancelAfter(job.SettleDelayMs + FrameTimeoutMs);
                    var result = await _hub.Clients.Client(connectionId).InvokeAsync<FrameResult>("CaptureFrame",
                        new FrameRequest(job.Id, seq, shot.Name, job.SettleDelayMs, job.Done, job.Total), timeout.Token);

                    if (result.Ok)
                    {
                        if (result.Width != job.Width || result.Height != job.Height)
                        {
                            throw new CaptureFailedException($"The camera's resolution changed from {job.Width}×{job.Height} to " +
                                $"{result.Width}×{result.Height} part way through, so the images wouldn't line up. Start the capture again.");
                        }
                        AcceptFrame(job, shot.Name, seq);
                        return;
                    }
                    problem = result.Error ?? "The capture page couldn't take a picture.";
                }
                catch (Exception ex) when (ex is not CaptureFailedException && !ct.IsCancellationRequested)
                {
                    _logger.LogDebug(ex, "Frame request for {Shot} failed", shot.Name);
                    problem = ex is OperationCanceledException && !connectionLifetime.IsCancellationRequested
                        ? "The capture page didn't respond in time."
                        : "Lost contact with the capture page.";
                }

                Publish(job, CaptureState.Running, $"{problem} Retrying…", paused: true);
                await Task.Delay(1000, ct);
                // Re-send the LEDs before trying again, in case anything changed while we waited (e.g. WLED was power-cycled)
                if (await ShowShotAsync(job, client, shot, ct) && shot.Led != null)
                {
                    await SettleExposureAsync(job, ct);
                }
            }
        }

        /// <summary>
        /// Discards a shot's frame so that it's captured again (at the end).
        /// </summary>
        private void ForgetShot(Job job, string name)
        {
            _logger.LogInformation("Will re-capture {Shot} as the tree may have changed while it was being taken", name);
            lock (job.Lock)
            {
                job.Captured.Remove(name);
                job.Saved.Remove(name);
                job.Outstanding.Remove(name);
                job.AcceptedSeq.Remove(name);
                _store.DeleteStaged(job.Direction, name); // so it isn't kept if the capture is stopped and resumed
            }
        }

        private void AcceptFrame(Job job, string name, long seq)
        {
            lock (job.Lock)
            {
                job.AcceptedSeq[name] = seq;
                job.Captured.Add(name);
                if (!job.Saved.Contains(name))
                {
                    job.Outstanding.Add(name);
                }
                // The upload can (rarely) arrive before we get here
                TryPromoteUpload(job, name);
            }
        }

        /// <summary>
        /// Called with the body of an image upload from the capture page.
        /// </summary>
        public async Task<UploadOutcome> SaveUploadAsync(string jobId, long seq, string name, Stream content, CancellationToken ct)
        {
            Job? job;
            lock (_sync) job = _job;
            if (job == null || job.Id != jobId || job.Kind != CaptureJobKind.Capture || job.Finished)
            {
                return new UploadOutcome(StatusCodes.Status409Conflict, "That capture isn't running any more.");
            }
            lock (job.Lock)
            {
                if (!job.ShotNames.Contains(name))
                {
                    return new UploadOutcome(StatusCodes.Status400BadRequest, $"'{name}' isn't one of this capture's images.");
                }
                if (job.Saved.Contains(name) || job.Committing)
                { // A repeat of an upload we already have (e.g. the page retried after a lost response)
                    return new UploadOutcome(StatusCodes.Status200OK);
                }
            }

            (int Width, int Height) size;
            try
            {
                size = await _store.WriteUploadAsync(job.Direction, name, seq, content, ct);
            }
            catch (InvalidDataException ex)
            {
                return new UploadOutcome(StatusCodes.Status400BadRequest, ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && !ct.IsCancellationRequested)
            {
                lock (job.Lock)
                {
                    if (job.Committing)
                    { // Too late to matter
                        return new UploadOutcome(StatusCodes.Status409Conflict, "That capture has finished.");
                    }
                }
                _logger.LogError(ex, "Couldn't save uploaded image {Name}", name);
                lock (job.Lock)
                {
                    job.UploadError ??= ex;
                    SignalUploads(job);
                }
                return new UploadOutcome(StatusCodes.Status500InternalServerError, $"Couldn't save the image: {ex.Message}");
            }

            if (size != (job.Width, job.Height))
            {
                _store.DeleteUpload(job.Direction, name, seq);
                return new UploadOutcome(StatusCodes.Status400BadRequest, $"The image is {size.Width}×{size.Height}, but this capture is {job.Width}×{job.Height}.");
            }

            lock (job.Lock)
            {
                job.LastUploadUtc = DateTime.UtcNow;
                TryPromoteUpload(job, name);
                SignalUploads(job);
            }
            return new UploadOutcome(StatusCodes.Status200OK);
        }

        /// <summary>
        /// Makes the upload for the frame we accepted for <paramref name="name"/> the staged image, if it's arrived. Only that
        /// frame's upload is used, so a late upload from a request we gave up on can never be mistaken for the current one.
        /// Must be called with job.Lock held.
        /// </summary>
        private void TryPromoteUpload(Job job, string name)
        {
            if (job.AcceptedSeq.TryGetValue(name, out var seq) && _store.TryPromoteUpload(job.Direction, name, seq))
            {
                job.Saved.Add(name);
                job.Outstanding.Remove(name);
            }
        }

        private async Task WaitForUploadCapacityAsync(Job job, CancellationToken ct)
        {
            var announced = false;
            while (true)
            {
                Task signal;
                lock (job.Lock)
                {
                    ThrowIfUploadFailed(job);
                    if (job.Outstanding.Count < MaxOutstandingUploads)
                    {
                        return;
                    }
                    if (DateTime.UtcNow - job.LastUploadUtc > UploadStallTimeout)
                    { // The page has stopped uploading (e.g. it was reloaded and lost its queue): they'll be re-captured at the end
                        job.Outstanding.Clear();
                        return;
                    }
                    signal = job.UploadSignal.Task;
                }

                if (!announced)
                {
                    Publish(job, CaptureState.Running, "Waiting for the capture page to catch up with uploading images…", paused: true);
                    announced = true;
                }
                await Task.WhenAny(signal, Task.Delay(1000, ct));
                ct.ThrowIfCancellationRequested();
            }
        }

        /// <summary>
        /// Waits for the last uploads, and re-captures any image that never arrived (e.g. the page was reloaded part way).
        /// </summary>
        private async Task EnsureEverythingSavedAsync(Job job, WledTreeClient client, List<Shot> shots, CancellationToken ct)
        {
            for (var round = 1; ; round++)
            {
                List<Shot> missing;
                while (true)
                {
                    Task signal;
                    lock (job.Lock)
                    {
                        ThrowIfUploadFailed(job);
                        missing = shots.Where(s => !job.Saved.Contains(s.Name)).ToList();
                        var stillExpected = missing.Any(s => job.Outstanding.Contains(s.Name)) && DateTime.UtcNow - job.LastUploadUtc <= UploadStallTimeout;
                        if (missing.Count == 0 || !stillExpected)
                        {
                            break;
                        }
                        signal = job.UploadSignal.Task;
                    }
                    Publish(job, CaptureState.Running, $"Waiting for the last {missing.Count} image(s) to upload…");
                    await Task.WhenAny(signal, Task.Delay(1000, ct));
                    ct.ThrowIfCancellationRequested();
                }

                if (missing.Count == 0)
                {
                    return;
                }
                if (round > MaxRecaptureRounds)
                {
                    throw new CaptureFailedException($"{missing.Count} image(s), e.g. {CapturedImageStore.FileName(missing[0].Name, job.Direction)}, " +
                        "still hadn't been received after re-capturing them. Check the capture page for upload errors.");
                }

                _logger.LogInformation("Re-capturing {Count} image(s) that weren't received", missing.Count);
                Publish(job, CaptureState.Running, $"Re-capturing {missing.Count} image(s) that need taking again…");
                lock (job.Lock)
                {
                    job.Outstanding.Clear();
                    job.LastUploadUtc = DateTime.UtcNow;
                }
                foreach (var shot in missing)
                {
                    await CaptureShotAsync(job, client, shot, ct);
                }
            }
        }

        private static bool IsCaptured(Job job, string name)
        {
            lock (job.Lock) return job.Captured.Contains(name);
        }

        private static void ThrowIfUploadFailed(Job job)
        {
            if (job.UploadError is Exception ex)
            {
                throw new CaptureFailedException($"Couldn't save the images on the server: {ex.Message}");
            }
        }

        private static void SignalUploads(Job job)
        {
            var signal = job.UploadSignal;
            job.UploadSignal = NewSignal();
            signal.TrySetResult();
        }

        #endregion

        #region Camera latency measurement

        /// <summary>
        /// Measures how long after the tree changes the camera's frames show the change, by switching every LED on and off
        /// a few times while the page watches the frames' brightness. Used to suggest a settle delay that's no longer than it
        /// needs to be (which is what decides how fast a capture runs) without risking images of the previous LED.
        /// </summary>
        private async Task RunLatencyAsync(Job job, WledTreeClient client, CancellationToken ct)
        {
            await EnsureTreeConnectedAsync(job, client);
            job.Brightness = await ReadBrightnessAsync(job, client, ct);
            job.Total = LatencyTrials;

            Publish(job, CaptureState.Running, "Turning the tree off and letting the camera settle…");
            await ShowShotAsync(job, client, Shot.AllOff, ct);
            await Task.Delay(1500, ct);

            var results = new List<LatencyProbeResult>();
            for (var trial = 0; trial < LatencyTrials; trial++)
            {
                Publish(job, CaptureState.Running, $"Measuring camera latency ({trial + 1} of {LatencyTrials})…");
                var (connectionId, connectionLifetime) = await WaitForCameraAsync(job, ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, connectionLifetime);
                timeout.CancelAfter(FrameTimeoutMs + LatencyWindowMs);

                LatencyProbeResult result;
                try
                {
                    var armed = await _hub.Clients.Client(connectionId).InvokeAsync<FrameResult>("LatencyArm", job.Id, timeout.Token);
                    if (!armed.Ok)
                    {
                        throw new CaptureFailedException(armed.Error ?? "The capture page couldn't start measuring.");
                    }

                    await ShowShotAsync(job, client, trial % 2 == 0 ? Shot.AllOn : Shot.AllOff, ct);

                    result = await _hub.Clients.Client(connectionId).InvokeAsync<LatencyProbeResult>("LatencyMeasure",
                        new LatencyProbeRequest(job.Id, LatencyWindowMs), timeout.Token);
                }
                catch (Exception ex) when (ex is not CaptureFailedException && !ct.IsCancellationRequested)
                {
                    _logger.LogDebug(ex, "Latency probe failed");
                    throw new CaptureFailedException("Lost contact with the capture page during the measurement. Try again.");
                }
                if (!result.Ok)
                {
                    throw new CaptureFailedException(result.Error ?? "The capture page couldn't measure the latency.");
                }
                results.Add(result);
                job.Done = trial + 1;
            }

            var full = results.Max(r => r.FullChangeMs);
            job.Latency = new LatencyResult(results.Count, results.Max(r => r.FirstChangeMs), full,
                results.Average(r => r.FrameIntervalMs), SuggestSettleDelay(full));
            Publish(job, CaptureState.Completed, $"Changes to the tree are fully visible to the camera {full:0} ms after the tree " +
                $"confirms them (worst of {results.Count}). Suggested settle delay: {job.Latency.SuggestedSettleDelayMs} ms.");
        }

        /// <summary>
        /// The settle delay to suggest for a measured worst-case latency: 20% + 15 ms of headroom, rounded up to 10 ms.
        /// </summary>
        public static int SuggestSettleDelay(double fullChangeMs) =>
            Math.Max(30, (int)Math.Ceiling((Math.Max(0, fullChangeMs) * 1.2 + 15) / 10) * 10);

        #endregion

        #region Tree helpers

        private async Task EnsureTreeConnectedAsync(Job job, WledTreeClient client)
        {
            if (client.IsConnected)
            {
                return;
            }

            Publish(job, CaptureState.Running, "Connecting to the tree…");
            await client.LoadStateAsync();
            if (!client.IsConnected)
            {
                throw new CaptureFailedException($"Can't connect to the tree (WLED): {client.lastException?.Message ?? "no response"}. " +
                    "Check it's powered on and that TreeURIBase points to it.");
            }
        }

        private async Task<byte?> ReadBrightnessAsync(Job job, WledTreeClient client, CancellationToken ct)
        {
            // Re-applied with every update so the images stay consistent even if WLED restarts (with its default brightness)
            string? json = null;
            await WithTreeRetriesAsync(job, async c => json = await client.GetJsonStateAsync(c), ct);
            try
            {
                var brightness = JsonNode.Parse(json!)?["bri"]?.GetValue<int>();
                return brightness is > 0 and <= 255 ? (byte)brightness.Value : null;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }

        private static async Task LightWholeTreeAsync(WledTreeClient client, byte? brightness, CancellationToken ct)
        {
            client.SetAllLeds(Colours.White);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TreeRequestTimeoutMs);
            await client.ApplyFullStateAsync(timeout.Token, brightness);
        }

        /// <summary>
        /// Keeps trying a request to WLED until it works (or the job is stopped), as WLED can drop off the network for a while
        /// or need power-cycling. The job carries on from where it was once WLED's back.
        /// </summary>
        private async Task<bool> WithTreeRetriesAsync(Job job, Func<CancellationToken, Task> request, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TreeRequestTimeoutMs);
                try
                {
                    await request(timeout.Token);
                    if (attempt > 1)
                    {
                        _logger.LogInformation("WLED is responding again after {Attempts} attempts", attempt);
                        Publish(job, CaptureState.Running, "The tree is responding again; carrying on.");
                    }
                    return attempt > 1;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    if (attempt == 1)
                    {
                        _logger.LogWarning(ex, "Request to WLED failed during image capture; retrying");
                    }
                    var reason = ex is OperationCanceledException ? "timed out" : ex.Message;
                    Publish(job, CaptureState.Running, $"The tree (WLED) isn't responding ({reason}). Retrying (attempt {attempt})… " +
                        "If this carries on, power-cycle the WLED controller; the capture will continue from where it got to.", paused: true);
                    await Task.Delay(Math.Min(250 << Math.Min(attempt - 1, 4), 3000), ct);
                }
            }
        }

        #endregion

        #region Status

        private static void RecordStep(Job job)
        {
            var now = job.Clock.Elapsed.TotalSeconds;
            if (job.LastStepAt > 0)
            {
                job.RecentStepSeconds.Enqueue(now - job.LastStepAt);
                if (job.RecentStepSeconds.Count > 25)
                {
                    job.RecentStepSeconds.Dequeue();
                }
            }
            job.LastStepAt = now;
            lock (job.Lock) job.Done = job.Captured.Count;
        }

        private void Publish(Job job, CaptureState state, string message, bool paused = false)
        {
            CaptureStatus status;
            lock (_sync)
            {
                if (_job != job || (job.Finished && state != job.State))
                { // Don't let anything overwrite the final state, or a newer job's status
                    return;
                }

                job.State = state;
                double? perImage = null;
                if (job.RecentStepSeconds.Count > 0)
                { // The median, so a pause doesn't throw the estimate out
                    var sorted = job.RecentStepSeconds.Order().ToArray();
                    perImage = sorted[sorted.Length / 2];
                }
                var remaining = Math.Max(0, job.Total - job.Done);
                status = new CaptureStatus
                {
                    Version = ++_statusVersion,
                    State = state,
                    Kind = job.Kind,
                    JobId = job.Id,
                    CameraClientId = job.ClientId,
                    Direction = job.Direction,
                    Done = job.Done,
                    Total = job.Total,
                    Paused = paused,
                    Message = message,
                    ElapsedSeconds = Math.Round(job.Clock.Elapsed.TotalSeconds, 1),
                    SecondsPerImage = perImage == null ? null : Math.Round(perImage.Value, 3),
                    EtaSeconds = perImage == null || job.Kind != CaptureJobKind.Capture ? null : Math.Round(remaining * perImage.Value),
                    Latency = job.Latency,
                };
                _status = status;
            }
            _ = SendStatusAsync(status);
        }

        private async Task SendStatusAsync(CaptureStatus status)
        {
            try
            {
                await _hub.Clients.All.SendAsync("CaptureStatus", status);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Couldn't send the capture status");
            }
        }

        private static string FormatDuration(TimeSpan duration) =>
            duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min {duration.Seconds} s" : $"{duration.TotalSeconds:0.0} s";

        private static StartResult Invalid(string error) => new(false, Error: error, Code: "invalid");

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        private readonly record struct Shot(string Name, int? Led, bool LightAll)
        {
            public static readonly Shot AllOn = new("AllOn", null, true);
            public static readonly Shot AllOff = new("AllOff", null, false);
            public static Shot ForLed(int led) => new(led.ToString(CultureInfo.InvariantCulture), led, false);
        }

        private sealed class Job(CaptureJobKind kind, string clientId)
        {
            public string Id { get; } = Guid.NewGuid().ToString("N");
            public CaptureJobKind Kind { get; } = kind;
            public string ClientId { get; } = clientId;
            public int Direction { get; init; }
            public int SettleDelayMs { get; init; }
            public int ExposureSettleMs { get; init; }
            public bool Resume { get; init; }
            public int Width { get; init; }
            public int Height { get; init; }
            public CancellationTokenSource StopRequested { get; } = new();
            public Stopwatch Clock { get; } = new();
            public byte? Brightness { get; set; }
            public CaptureState State { get; set; } = CaptureState.Queued; // guarded by the coordinator's _sync
            public bool Finished => State is CaptureState.Completed or CaptureState.Failed or CaptureState.Cancelled;

            // Only touched by the job itself
            public bool NeedExposureSettle { get; set; } = true;
            public string? LastCapturedShot { get; set; }
            public Queue<double> RecentStepSeconds { get; } = new();
            public double LastStepAt { get; set; }
            public LatencyResult? Latency { get; set; }
            public long LastSeq;
            public int Done;
            public int Total;

            // Shared between the job and image uploads, so guarded by Lock
            public object Lock { get; } = new();
            public HashSet<string> ShotNames { get; } = [];
            public HashSet<string> Captured { get; } = [];  // a frame has been accepted (or it was already saved, when resuming)
            public Dictionary<string, long> AcceptedSeq { get; } = []; // which frame request's upload to use for each image
            public HashSet<string> Saved { get; } = [];     // safely in the staging folder
            public HashSet<string> Outstanding { get; } = []; // accepted, but not uploaded yet
            public bool Committing { get; set; }
            public DateTime LastUploadUtc { get; set; } = DateTime.UtcNow;
            public TaskCompletionSource UploadSignal { get; set; } = NewSignal();
            public Exception? UploadError { get; set; }
        }
    }
}
