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

            public void Update()
            {
                NewState.CopyTo(CurrentState, 0);
            }

            public LedUpdate[] GetLEDChanges()
            {
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

        public WledClient(string uriBase, TimeSpan timeout, bool useDDP = false, string? coords = null)
        {
            if (useDDP)
            {
                _ddpClient = new DdpClient(new Uri(uriBase).Host);
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

            var segmentJson = wledStatusJson["seg"]![0]!; // TODO: Loop through every LED in every segment properly

            _treeState = new TreeState(new RGBValue[(int)segmentJson["stop"]! - (int)segmentJson["start"]!])
            {
                LedsStart = (int)segmentJson["start"]!,
                LedsEnd = (int)segmentJson["stop"]!
            };
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

        public async Task<string> GetJsonStateAsync() => await _client.GetStringAsync("state");
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
                while (true)
                {
                    var attempt = 0;
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
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            var diffBefore = now - _lastUpdate;

            if (diffBefore < delayBeforeMS || now < _minTicksForNextUpdate)
            {
                // If it's not been long enough since the last update, or we're not yet at a time when we can do another update, wait a bit
                var delay = (int)Math.Max(delayBeforeMS - diffBefore, _minTicksForNextUpdate - now);
                await DelayNoException(delay, ct);
                // Since we waited, update 'now'
                now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            var changes = _treeState!.GetLEDChanges();

            if (changes.Length != 0)
            {
                // We have the list of LEDs that need to be changed.
                if (_ddpClient != null)
                {
                    await _ddpClient.SendPixels(changes);
                }
                else
                {
                    _colourSetObject.seg.i = changes.SelectMany(c => new object[] { c.LedIndex, c.NewColour.ToHex() }).ToArray();
                    await SendCommand(_colourSetObject);
                    // TODO: Check number of changes / max json command length
                }

                _treeState.Update();
            }

            _minTicksForNextUpdate = now + delayAfterMS;
            return changes;
        }

        public async Task Reboot()
        {
            await SendCommand(new { rb = true });
        }

        public async Task SetOnOff(bool on, byte? brightness = null)
        {
            var liveObject = (dynamic)new ExpandoObject();
            liveObject.on = on;
            if (brightness != null)
            {
                liveObject.bri = brightness;
            }
            await SendCommand(liveObject);
        }

        public async ValueTask RestoreState()
        {
            if (_savedState != null)
            {
                await SendCommand(_savedState); // Send back the same state we had initially (if we got that far)
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
