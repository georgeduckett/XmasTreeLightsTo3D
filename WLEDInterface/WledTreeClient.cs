using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Numerics;

namespace WLEDInterface
{
    public class WledTreeClient : IAsyncDisposable
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
        private ReadOnlyCollection<Vector3> _ledCoordinates;
        private readonly TreeState _treeState;

        private readonly dynamic _colourSetObject;

        private string _savedState = null;

        public int LedIndexStart => _treeState.LedsStart;
        public int LedIndexEnd => _treeState.LedsEnd;

        public WledTreeClient(string ipAddress, TimeSpan timeout, string coords)
        {
            _client = new HttpClient
            {
                BaseAddress = new Uri($"http://{ipAddress}/json/"),
                Timeout = timeout
            };
            _ledCoordinates = coords.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l) && !l.Contains("index")).Select(line => new Vector3(line.Trim().Trim('[', ']').Split(',').Where(s => float.TryParse(s, out _)).Select(s => float.Parse(s)).Skip(1).Take(3).ToArray().AsSpan())).ToArray().AsReadOnly();

            _treeState = new TreeState(new RGBValue[_ledCoordinates.Count]);


            _colourSetObject = new ExpandoObject();
            _colourSetObject.seg = (dynamic)new ExpandoObject();
            _colourSetObject.seg.id = 0;
        }

        public async Task LoadStateAsync()
        {
            _savedState = await GetJsonStateAsync();
            var wledStatusJson = JsonNode.Parse(_savedState)!;

            var segmentJson = wledStatusJson["seg"]![0]!; // TODO: Loop through every LED in every segment properly

            _treeState.LedsStart = (int)segmentJson["start"]!;
            _treeState.LedsEnd = (int)segmentJson["stop"]!;

            if (_ledCoordinates.Count != _treeState.LedCount)
            {
                throw new InvalidDataException($"The number of LEDs (in segment one, the only supported segment), {_treeState.LedCount} does not match the number of LED coordinates we have, {_ledCoordinates.Count}.");
            }
        }

        public ReadOnlyCollection<Vector3> LedCoordinates => _ledCoordinates;

        public async Task<string> GetJsonStateAsync()
        {
            return await _client.GetStringAsync("state");
        }
        private async Task<string> SendCommand(dynamic commandObject)
        {
            return await SendCommand(JsonSerializer.Serialize(commandObject));
        }
        private async Task<string> SendCommand(string jsonCommand)
        {
            var commandContent = new StringContent(jsonCommand, Encoding.UTF8, "application/json");
            commandContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var result = await _client.PostAsync(string.Empty, commandContent);
            var resultString = await result.Content.ReadAsStringAsync();
            result.EnsureSuccessStatusCode();

            return resultString;
        }

        public void SetLedsColours(params LedUpdate[] ledUpdates)
        {
            foreach (var ledUpdate in ledUpdates)
            {
                _treeState.NewState[ledUpdate.LedIndex] = ledUpdate.NewColour;
            }
        }
        public void SetLedsColours(Func<Vector3, RGBValue> funcLedColour)
        {
            SetLedsColours(LedCoordinates.Select((c, i) => new LedUpdate(i, funcLedColour(c))).ToArray());
        }
        public void SetLedColour(int ledIndex, RGBValue ledColour)
        {
            SetLedsColours(new LedUpdate(ledIndex, ledColour));
        }

        public void SetAllLeds(RGBValue colour)
        {
            SetLedsColours(Enumerable.Range(_treeState.LedsStart, _treeState.LedCount).Select(i => new LedUpdate(i, colour)).ToArray());
        }

        public async Task ApplyUpdate()
        {
            var changes = _treeState.GetLEDChanges();

            if (changes.Length == 0) return;

            // We have the list of LEDs that need to be changed. Work out what method to best change them. (for now just send the JSON).
            _colourSetObject.seg.i = changes.SelectMany(c => new object[] { c.LedIndex, c.NewColour.ToHex() }).ToArray();
            await SendCommand(_colourSetObject);
            // TODO: Check number of changes / max json command length
            _treeState.Update();

            return;
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

        public async ValueTask DisposeAsync()
        {
            //await SetLive(false);
            await SendCommand(_savedState); // Send back the same state we had initially
            ((IDisposable)_client).Dispose();
        }
    }
}
