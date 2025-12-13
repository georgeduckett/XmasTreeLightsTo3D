using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Dynamic;
using System.Net.Http.Headers;
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

        public WledClient(string uriBase, TimeSpan timeout, string? coords = null)
        {
            _client = new HttpClient
            {
                BaseAddress = new Uri($"{uriBase}/json/"),
                Timeout = timeout
            };
            _colourSetObject = new ExpandoObject();
            _colourSetObject.seg = (dynamic)new ExpandoObject();
            _colourSetObject.seg.id = 0;
        }

        public async Task LoadStateAsync()
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

            var segmentJson = wledStatusJson["seg"]![0]!; // TODO: Loop through every LED in every segment properly

            _treeState = new TreeState(new RGBValue[(int)segmentJson["stop"]! - (int)segmentJson["start"]!])
            {
                LedsStart = (int)segmentJson["start"]!,
                LedsEnd = (int)segmentJson["stop"]!
            };

            /*if (LedCoordinates!.Count != 0 && LedCoordinates.Count != _treeState.LedCount)
            {
                throw new InvalidDataException($"The number of LEDs (in segment one, the only supported segment), {_treeState.LedCount} does not match the number of LED coordinates we have, {LedCoordinates.Count}.");
            }*/
        }

        public async Task<string> GetJsonStateAsync() => await _client.GetStringAsync("state");
        private async Task<string> SendCommand(dynamic commandObject)
        {
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
                try
                {
                    var result = await _client.PostAsync(string.Empty, commandContent);
                    var resultString = await result.Content.ReadAsStringAsync();
                    result.EnsureSuccessStatusCode();
                    return resultString;
                }
                catch (Exception ex) when (attempt < 10) { }
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
                // We have the list of LEDs that need to be changed. Work out what method to best change them. (for now just send the JSON).
                _colourSetObject.seg.i = changes.SelectMany(c => new object[] { c.LedIndex, c.NewColour.ToHex() }).ToArray();
                await SendCommand(_colourSetObject);
                // TODO: Check number of changes / max json command length
                _treeState.Update();
            }

            _minTicksForNextUpdate = now + delayAfterMS;
            return changes;
        }

        public async Task Reboot()
        {
            await SendCommand(new { rb = true });
        }

        public async Task SetLive(bool live, byte? brightness = null)
        {
            var liveObject = (dynamic)new ExpandoObject();
            liveObject.live = live;
            if (brightness != null)
            {
                liveObject.bri = brightness;
            }
            await SendCommand(liveObject);
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

        public async ValueTask DisposeAsync()
        {
            //await SetLive(false);
            if (_savedState != null)
            {
                await SendCommand(_savedState); // Send back the same state we had initially (if we got that far)
                await Reboot(); // Then reboot since it doesn't seem controllable from anything else until we do that
            }
            ((IDisposable)_client).Dispose();
        }
    }
}
