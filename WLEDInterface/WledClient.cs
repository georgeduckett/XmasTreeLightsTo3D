using System.Buffers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Dynamic;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static WLEDInterface.WledClient;

namespace WLEDInterface
{
    public class WledClient : IAsyncDisposable
    {
        public record LedUpdate(int LedIndex, RGBValue NewColour);
        private class TreeState
        {
            public RGBValue[] CurrentState { get; set; }
            public RGBValue[] NewState { get; set; }
            public int LedsStart { get; set; }
            public int LedsEnd { get; set; }
            public int LedCount => LedsEnd - LedsStart;
            public TreeState(RGBValue[] initialState)
            {
                CurrentState = initialState;
                NewState = new RGBValue[initialState.Length];
                initialState.CopyTo(NewState, 0);
            }

            /// <summary>
            /// Set when we no longer know what the LEDs are showing (e.g. after restoring WLED's own state, which unfreezes the
            /// segment), so the next update sends every LED rather than only the ones we think have changed.
            /// </summary>
            private bool _currentStateUnknown;

            public void Invalidate()
            {
                _currentStateUnknown = true;
            }

            public void Update()
            {
                NewState.CopyTo(CurrentState, 0);
                _currentStateUnknown = false;
            }

            public LedUpdate[] GetLEDChanges()
            {
                if (_currentStateUnknown)
                {
                    return NewState.Select((colour, index) => new LedUpdate(index, colour)).ToArray();
                }

                return NewState.Zip(CurrentState)
                    .Select((State, Index) => new { State, Index })
                    .Where(led => led.State.First != led.State.Second)
                    .Select(led => new LedUpdate(led.Index, led.State.First))
                    .ToArray();
            }
        }
        private readonly HttpClient _client;
        private readonly DdpClient? _ddpClient;
        private TreeState? _treeState;

        private readonly dynamic _colourSetObject;

        private string? _savedState = null;

        public int LedIndexStart => _treeState!.LedsStart;
        public int LedIndexEnd => _treeState!.LedsEnd;

        /// <summary>
        /// When we last applied an update
        /// </summary>
        private long _lastUpdate = 0;
        private long _minTicksForNextUpdate = 0;
        public bool IsConnected { get; private set; } = false;
        public Exception? lastException = null;

        private long _startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        private bool _treeIsOn = false;
        private bool _useDDP => _ddpClient != null;

        /// <summary>
        /// With DDP, at most one frame is sent this often, so an animation updating in a tight loop (e.g. <see cref="FadeLight"/>)
        /// can't flood WLED with packets. 20ms is 50 frames a second, faster than ~900 LEDs on one data line can be refreshed.
        /// </summary>
        public int MinimumFrameIntervalMS { get; set; } = 20;

        /// <summary>The gamma correction applied to colours sent with DDP (1 if none), to match what WLED does with JSON updates.</summary>
        public double RealtimeGamma { get; private set; } = 1;

        /// <summary>Whether LED colours are sent with DDP (otherwise with WLED's JSON API).</summary>
        public bool UsesDdp => _ddpClient != null;

        /// <summary>
        /// The dimmest level (0-255) a lit LED is shown at, as the LEDs get it, or 0 to show every level as it is. Very dim
        /// LEDs can't show colours properly (they tend to look red), so ones that would be dimmer than
        /// <see cref="SwitchOffBelow"/> are switched off and the rest are brightened, keeping their colour (see
        /// <see cref="DdpClient.DimmestLevel"/>). WLED's own brightness is allowed for. Only used with DDP.
        /// </summary>
        public int DimmestLevel => _dimmestLevel;

        /// <summary>Where LEDs are switched off rather than brightened up to <see cref="DimmestLevel"/>, as a fraction of it.</summary>
        public double SwitchOffBelow => _switchOffBelow;

        /// <summary>Sets <see cref="DimmestLevel"/> and <see cref="SwitchOffBelow"/> (a change shows straight away).</summary>
        public void SetDimmestLevel(int level, double switchOffBelow)
        {
            _dimmestLevel = Math.Clamp(level, 0, 255);
            _switchOffBelow = Math.Clamp(switchOffBelow, 0.01, 1);
            UpdateDimmestLevel();
        }

        /// <summary>
        /// How much WLED dims the colours sent with DDP (1 for not at all): by its brightness, scaled by its brightness factor
        /// (and gamma corrected, if it does that), unless it's set to use full brightness for realtime data.
        /// </summary>
        public double RealtimeBrightness { get; private set; } = 1;

        private int _dimmestLevel;
        private double _switchOffBelow = 0.25;
        private int _wledBrightness = 255;
        private bool _forceMaxBrightness;
        private int _brightnessFactorPercent = 100;
        private double _brightnessGamma = 1;
        private long _lastBrightnessCheck;
        private int _checkingBrightness;

        // WLED leaves realtime (DDP) mode, and goes back to its own effect, if it doesn't get a packet for a couple of seconds
        // (its realtime 'Timeout' setting). While an animation is running the last frame is resent often enough to stop that
        // happening when the animation pauses (e.g. Snake between games); once it finishes WLED is left to time out.
        private readonly CancellationTokenSource _keepAliveCancellation = new();
        private readonly Task? _keepAliveTask;
        private readonly SemaphoreSlim _frameLock = new(1, 1);
        private volatile bool _holdRealtime;
        private long _lastFrameSent = long.MinValue / 2;
        private int _realtimeTimeoutMS = 2500;
        private int _keepAliveMS = 1000;

        public WledClient(string uriBase, TimeSpan timeout, bool useDDP = false)
        {
            if (useDDP)
            {
                _ddpClient = new DdpClient(new Uri(uriBase).Host);
                _keepAliveTask = Task.Run(() => KeepRealtimeAliveAsync(_keepAliveCancellation.Token));
            }
            else
            {
                _ddpClient = null;
            }

            _client = new HttpClient
            {
                BaseAddress = new Uri($"{uriBase}/json/"),
                Timeout = timeout
            };
            _colourSetObject = new ExpandoObject();
            _colourSetObject.seg = (dynamic)new ExpandoObject();
            _colourSetObject.seg.id = 0;
        }

        public virtual async Task LoadStateAsync()
        {
            try
            {
                _savedState = await GetJsonStateAsync();
                IsConnected = true;
            }
            catch (Exception ex)
            {
                lastException = ex;
                IsConnected = false;
                _savedState = @"{""seg"":[{""start"":0,""stop"":695,""len"":695}]}";
            }
            var wledStatusJson = JsonNode.Parse(_savedState)!;

            _treeIsOn = wledStatusJson["on"] != null && (bool)wledStatusJson["on"]!;
            ReadBrightness(wledStatusJson);

            if (wledStatusJson["live"] != null)
            {
                // If WLED was showing one of our DDP animations when we read its state, sending that state back (to restore
                // it) mustn't put WLED back into realtime mode; "live": false makes it leave it straight away
                wledStatusJson["live"] = false;
                _savedState = wledStatusJson.ToJsonString();
            }

            var segmentJson = wledStatusJson["seg"]![0]!; // TODO: Loop through every LED in every segment properly

            _treeState = new TreeState(new RGBValue[(int)segmentJson["stop"]! - (int)segmentJson["start"]!])
            {
                LedsStart = (int)segmentJson["start"]!,
                LedsEnd = (int)segmentJson["stop"]!
            };

            if (_ddpClient != null)
            {
                await ConfigureRealtimeAsync();
            }
        }

        /// <summary>
        /// Reads WLED's settings to send DDP frames that look the same as JSON updates did: by default WLED gamma corrects
        /// colours set through its JSON API, but not realtime data (the 'Disable realtime gamma correction' setting), so we
        /// apply WLED's gamma ourselves. Also reads how long WLED waits for realtime data before giving up on it.
        /// </summary>
        private async Task ConfigureRealtimeAsync()
        {
            var gamma = 2.2; // WLED's default
            var timeoutMS = 2500;
            var source = "WLED's defaults (couldn't read its settings)";
            if (IsConnected)
            {
                try
                {
                    var cfg = JsonNode.Parse(await _client.GetStringAsync("cfg"))!;
                    var live = cfg["if"]?["live"];
                    var gammaCorrectsRealtime = live?["no-gc"] is JsonValue noGc && noGc.TryGetValue(out bool disabled) && !disabled;
                    var colourGamma = ReadGamma(cfg["light"]?["gc"]?["col"], cfg["light"]?["gc"]?["val"]);
                    // How WLED's brightness applies to realtime data (see RealtimeBrightness)
                    _forceMaxBrightness = live?["maxbri"] is JsonValue maxBri && maxBri.TryGetValue(out bool forceMax) && forceMax;
                    _brightnessFactorPercent = cfg["light"]?["scale-bri"] is JsonValue factor && factor.TryGetValue(out int percent) ? Math.Max(1, percent) : 100;
                    _brightnessGamma = ReadBrightnessGamma(cfg["light"]?["gc"]?["bri"], cfg["light"]?["gc"]?["val"]);
                    // If WLED gamma corrects realtime data itself, or doesn't gamma correct colours at all, send them as they are
                    gamma = gammaCorrectsRealtime ? 1 : colourGamma;
                    if (live?["timeout"] is JsonValue timeout && timeout.TryGetValue(out int tenths) && tenths > 0)
                    {
                        timeoutMS = tenths * 100;
                    }
                    source = "WLED's settings";
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            RealtimeGamma = gamma;
            _ddpClient!.Gamma = gamma;
            _realtimeTimeoutMS = timeoutMS;
            _keepAliveMS = Math.Clamp(timeoutMS / 2, 200, 1000);
            UpdateDimmestLevel();
            Console.WriteLine($"Sending LED colours with DDP: gamma {gamma:0.##}, resending the last frame every {_keepAliveMS}ms during pauses, " +
                              $"WLED dims them to {RealtimeBrightness:0%} (from {source}).");
        }

        private static double ReadBrightnessGamma(JsonNode? brightness, JsonNode? value)
        {
            // A gamma (1 if brightness isn't corrected, which is WLED's default), or in older versions whether it is
            if (brightness is JsonValue b)
            {
                if (b.TryGetValue(out double g)) return g > 1 && g <= 3 ? g : 1;
                if (b.TryGetValue(out bool on) && on) return value is JsonValue v && v.TryGetValue(out double gv) && gv > 1 && gv <= 3 ? gv : 2.8;
            }
            return 1;
        }

        private void ReadBrightness(JsonNode state)
        {
            // (WLED gives the brightness it'll use when it's switched on, even while it's off)
            if (state["bri"] is JsonValue bri && bri.TryGetValue(out int brightness) && brightness != _wledBrightness)
            {
                _wledBrightness = Math.Clamp(brightness, 0, 255);
                UpdateDimmestLevel();
            }
        }

        /// <summary>
        /// Reads WLED's brightness again (it's read when an animation's running every few seconds anyway), in case it's been
        /// changed (it dims the colours sent with DDP, which the dimmest level allows for).
        /// </summary>
        public async Task RefreshBrightnessAsync()
        {
            if (Interlocked.Exchange(ref _checkingBrightness, 1) == 1)
            {
                return;
            }
            try
            {
                ReadBrightness(JsonNode.Parse(await GetJsonStateAsync())!);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
            finally
            {
                Interlocked.Exchange(ref _lastBrightnessCheck, NowMS());
                Interlocked.Exchange(ref _checkingBrightness, 0);
            }
        }

        /// <summary>
        /// Works out the dimmest level to send for the LEDs to get <see cref="DimmestLevel"/> once WLED's dimmed them, and
        /// resends what's on the tree so a change shows straight away.
        /// </summary>
        private void UpdateDimmestLevel()
        {
            var brightness = _forceMaxBrightness ? 255 : _wledBrightness;
            brightness = Math.Min(255, brightness * _brightnessFactorPercent / 100);
            RealtimeBrightness = Math.Max(1, _brightnessGamma > 1 ? Math.Round(Math.Pow(brightness / 255.0, _brightnessGamma) * 255) : brightness) / 255.0;
            if (_ddpClient == null)
            {
                return;
            }
            _ddpClient.DimmestLevel = _dimmestLevel == 0 ? 0 : (float)Math.Min(255, _dimmestLevel / RealtimeBrightness);
            _ddpClient.SwitchOffBelow = (float)_switchOffBelow;
            if (_holdRealtime && _treeState != null)
            {
                _ = ResendFrameAsync();
            }
        }

        private async Task ResendFrameAsync()
        {
            try
            {
                await SendFrameAsync(newFrame: false);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        private static double ReadGamma(JsonNode? colour, JsonNode? value)
        {
            // Newer versions give the gamma used for colours (1 if they aren't corrected); older ones just say whether they are
            if (colour is JsonValue c)
            {
                if (c.TryGetValue(out double g)) return g > 0.1 && g <= 3 ? g : 1;
                if (c.TryGetValue(out bool on)) return on ? (value is JsonValue v && v.TryGetValue(out double gv) ? gv : 2.8) : 1;
            }
            return 2.2;
        }

        public async Task<RGBValue[]?> GetLiveLEDState()
        {
            var uri = new Uri($"ws://{_client.BaseAddress!.Host}/ws");

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(uri, CancellationToken.None);

            // Request live LED stream
            var request = Encoding.UTF8.GetBytes("{'lv':true}");
            await ws.SendAsync(request, WebSocketMessageType.Text, true, CancellationToken.None);

            var buffer = new byte[8192];

            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("Server closed connection.");
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "OK", CancellationToken.None);
                    break;
                }
                else if(result.MessageType == WebSocketMessageType.Binary)
                {
                    if (buffer[0] != 76) continue;

                    var startIndex = buffer[1] == 4 ? 4 : 2;

                    var colours = new List<RGBValue>();
                    for (var i = startIndex; i < result.Count; i += 3)
                    {
                        colours.Add(new RGBValue(buffer[i], buffer[i + 1], buffer[i + 2]));
                    }

                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
            }

            return null;
        }

        public async Task<string> GetJsonStateAsync(CancellationToken ct = default) => await _client.GetStringAsync("state", ct);
        public async Task<string> GetJsonEffectsAsync() => await _client.GetStringAsync("effects");
        private async Task<string> SendCommand(dynamic commandObject)
        {
            if (!_treeIsOn)
            {
                commandObject.on = true;
                _treeIsOn = true;
            }

            return await SendCommand(JsonSerializer.Serialize(commandObject));
        }
        private async Task<string> SendCommand(string jsonCommand)
        {
            if (IsConnected)
            { // If we're connected send the command, otherwise don't
                var commandContent = new StringContent(jsonCommand, Encoding.UTF8, "application/json");
                commandContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                // Retry up to 10 times if it fails
                var attempt = 0;
                while (true)
                {
                    try
                    {
                        var result = await _client.PostAsync(string.Empty, commandContent);
                        var resultString = await result.Content.ReadAsStringAsync();
                        result.EnsureSuccessStatusCode();
                        return resultString;
                    }
                    catch (Exception) when (attempt++ < 10) { }
                }
            }

            return string.Empty;
        }

        public void SetLedsColours(params LedUpdate[] ledUpdates)
        {
            foreach (var ledUpdate in ledUpdates)
            {
                _treeState!.NewState[ledUpdate.LedIndex] = ledUpdate.NewColour;
            }
        }
        public void SetLedColour(int ledIndex, RGBValue ledColour)
        {
            _treeState!.NewState[ledIndex] = ledColour;
        }
        public RGBValue GetLedColour(int ledIndex)
        {
            return _treeState!.NewState[ledIndex];
        }
        public async Task FadeLight(int lightIndex, int speedOfLightsMS, RGBValue to, CancellationToken cancellationToken)
        {
            var from = GetLedColour(lightIndex);
            // Fade out the previous letter
            var startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var endTime = startTime + speedOfLightsMS;

            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (now >= endTime)
                {
                    break;
                }

                var progress = (now - startTime) / (double)(endTime - startTime);
                var colour = ((to - from) * progress) + from;
                SetLedColour(lightIndex, (RGBValue)colour);
                await ApplyUpdate(cancellationToken);
            }
            // Make sure it's fully set to the target colour
            SetLedColour(lightIndex, to);
            await ApplyUpdate(cancellationToken);
        }
        public void SetAllLeds(RGBValue colour)
        {
            SetLedsColours(Enumerable.Range(_treeState!.LedsStart, _treeState.LedCount).Select(i => new LedUpdate(i, colour)).ToArray());
        }
        private static async Task DelayNoException(int delay, CancellationToken ct)
        {
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException) { }
        }
        /// <summary>
        /// Applys the update to the tree, ensuring we wait the correct amount of time between updates. Returns when the tree has been updated.
        /// </summary>
        /// <param name="delayBeforeMS">How long to ensure we waited between the previous ApplyUpdate and this one</param>
        /// <param name="delayAfterMS">How long to ensure we wait between the next ApplyUpdate and this one</param>
        /// <returns>The leds that have changed, and their colours</returns>
        public async Task<LedUpdate[]> ApplyUpdate(CancellationToken ct, int delayBeforeMS = 0, int delayAfterMS = 0)
        {
            var now = NowMS();

            if (_ddpClient != null && now - Interlocked.Read(ref _lastFrameSent) < _realtimeTimeoutMS)
            {
                // An animation is (still) showing our frames, so keep WLED in realtime mode while we wait below
                _holdRealtime = true;
            }

            var waitUntil = Math.Max(_lastUpdate + delayBeforeMS, _minTicksForNextUpdate);
            if (now < waitUntil)
            {
                // If it's not been long enough since the last update, or we're not yet at a time when we can do another update, wait a bit
                await DelayNoException((int)(waitUntil - now), ct);
                // Since we waited, update 'now'
                now = NowMS();
            }

            var changes = _treeState!.GetLEDChanges();
            // With DDP the frame's also sent if WLED isn't showing our frames (it's gone back to its own effect after its
            // realtime timeout), e.g. for an animation that's just started with the same colours the last one finished with
            var notShowing = _ddpClient != null && !_holdRealtime;

            if (changes.Length != 0 || notShowing)
            {
                // We have the list of LEDs that need to be changed.
                if (_ddpClient != null)
                {
                    var nextFrame = Interlocked.Read(ref _lastFrameSent) + MinimumFrameIntervalMS;
                    if (now < nextFrame)
                    {
                        await DelayNoException((int)(nextFrame - now), ct);
                        now = NowMS();
                    }
                    // Send every LED, not just the changes: it's only a couple of packets
                    await SendFrameAsync(newFrame: true);
                }
                else
                {
                    _colourSetObject.seg.i = changes.SelectMany(c => new object[] { c.LedIndex, c.NewColour.ToHex() }).ToArray();
                    await SendCommand(_colourSetObject);
                    // TODO: Check number of changes / max json command length
                    _treeState.Update();
                }
            }

            _lastUpdate = now;
            _minTicksForNextUpdate = now + delayAfterMS;
            return changes;
        }

        private static long NowMS() => Environment.TickCount64;

        /// <summary>
        /// Sends the frame we've built up (<paramref name="newFrame"/>) or resends the last one, with DDP.
        /// </summary>
        private async Task SendFrameAsync(bool newFrame)
        {
            await _frameLock.WaitAsync();
            try
            {
                var treeState = _treeState!;
                if (newFrame)
                {
                    await _ddpClient!.SendFrame(treeState.NewState, treeState.LedsStart);
                    treeState.Update();
                    _holdRealtime = true;
                }
                else
                {
                    await _ddpClient!.SendFrame(treeState.CurrentState, treeState.LedsStart);
                }
                Interlocked.Exchange(ref _lastFrameSent, NowMS());
            }
            finally
            {
                _frameLock.Release();
            }
        }

        private async Task KeepRealtimeAliveAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    // While an animation's running, check WLED's brightness every few seconds in case it's changed
                    if (_holdRealtime && NowMS() - Interlocked.Read(ref _lastBrightnessCheck) > 5000)
                    {
                        _ = RefreshBrightnessAsync();
                    }
                    if (!_holdRealtime || _treeState == null || NowMS() - Interlocked.Read(ref _lastFrameSent) < _keepAliveMS)
                    {
                        continue;
                    }
                    try
                    {
                        await SendFrameAsync(newFrame: false);
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// Stops resending the last frame (with DDP) once an animation has finished, so WLED goes back to its own effect
        /// after its realtime timeout (a couple of seconds).
        /// </summary>
        public void ReleaseRealtime()
        {
            _holdRealtime = false;
        }

        /// <summary>
        /// Sends the complete desired state of every LED (not just the ones that changed since the last update) in a single,
        /// run-length encoded request, e.g. "all black except LED 42" is sent as <c>[0,42,"000000",42,"FFFFFF",43,560,"000000"]</c>.
        /// Because it doesn't depend on what we think the tree is currently showing, it also corrects any LEDs that are out of
        /// sync (e.g. after a failed request, or WLED being power-cycled). It always turns the tree on.
        /// Unlike <see cref="ApplyUpdate"/> it makes a single attempt, honours <paramref name="ct"/>, and throws if the update
        /// can't be sent (including when we're not connected), so the caller can decide how to retry.
        /// </summary>
        /// <param name="brightness">Optional brightness to apply with the update, e.g. to keep it the same if WLED restarts.</param>
        public async Task ApplyFullStateAsync(CancellationToken ct, byte? brightness = null)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to WLED.");
            }

            ReleaseRealtime();
            var state = _treeState!.NewState;
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("on", true);
                if (_ddpClient != null)
                {
                    // Leave realtime (DDP) mode, or WLED would keep showing the last animation frame instead of this
                    writer.WriteBoolean("live", false);
                }
                if (brightness != null)
                {
                    writer.WriteNumber("bri", brightness.Value);
                }
                writer.WriteStartObject("seg");
                writer.WriteNumber("id", 0);
                writer.WriteStartArray("i");
                for (var start = 0; start < state.Length;)
                {
                    var end = start + 1;
                    while (end < state.Length && state[end] == state[start]) end++;

                    writer.WriteNumberValue(start);
                    if (end - start > 1)
                    {
                        writer.WriteNumberValue(end); // WLED treats [start, stop, colour] as the range start..stop-1
                    }
                    writer.WriteStringValue(state[start].ToHex());
                    start = end;
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            using var content = new ByteArrayContent(buffer.WrittenSpan.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await _client.PostAsync(string.Empty, content, ct);
            response.EnsureSuccessStatusCode();

            _treeIsOn = true;
            _treeState.Update();
        }

        public async Task Reboot()
        {
            dynamic cmd = new ExpandoObject();
            cmd.rb = true;
            await SendCommand(cmd);
            _treeState?.Invalidate();
        }

        public async Task SetOnOff(bool on, byte? brightness = null)
        {
            var liveObject = (dynamic)new ExpandoObject();
            liveObject.on = on;
            if (brightness != null)
            {
                liveObject.bri = brightness;
            }
            if (_ddpClient != null)
            {
                ReleaseRealtime();
                liveObject.live = false; // leave realtime (DDP) mode straight away
            }
            await SendCommand(liveObject);
            _treeState?.Invalidate(); // WLED unfreezes segments when turned on, so we no longer know what the LEDs show
        }

        public async ValueTask RestoreState()
        {
            ReleaseRealtime();
            if (_savedState != null)
            {
                await SendCommand(_savedState); // Send back the same state we had initially (if we got that far)
                _treeState?.Invalidate(); // WLED is back to running its own effect, so the next update needs to set every LED
            }
        }

        public async Task<string?> CurrentEffect()
        {
            var stateJson = JsonNode.Parse(_savedState!)!;
            var segmentJson = stateJson["seg"]![0]!; // TODO: Loop through every LED in every segment properly
            var effectId = segmentJson["fx"]?.GetValue<int>();

            if (effectId == null)
            {
                return null;
            }

            var infoJson = JsonNode.Parse(await GetJsonEffectsAsync())!;
            var effectsArray = infoJson!.AsArray();
            if (effectId < 0 || effectId >= effectsArray.Count)
            {
                return null;
            }
            return effectsArray[effectId.Value]?.GetValue<string>();
        }

        public async ValueTask DisposeAsync()
        {
            ReleaseRealtime();
            _keepAliveCancellation.Cancel();
            if (_keepAliveTask != null)
            {
                await _keepAliveTask;
            }
            //await SetLive(false);
            if (_savedState != null)
            {
                await SendCommand(_savedState); // Send back the same state we had initially (if we got that far)
                await Reboot(); // Then reboot since it doesn't seem controllable from anything else until we do that
            }
            _client.Dispose();
            _ddpClient?.Dispose();
        }
    }
}
