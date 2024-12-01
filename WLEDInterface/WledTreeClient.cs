using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Collections.ObjectModel;
using System.Dynamic;

namespace WLEDInterface
{
    public class WledTreeClient : IAsyncDisposable
    {
        private readonly HttpClient _client;
        private int _ledsStart;
        private int _ledsEnd;
        private ReadOnlyCollection<ThreeDPoint> _ledCoordinates;

        public WledTreeClient(string ipAddress, TimeSpan timeout, string coords)
        {
            _client = new HttpClient
            {
                BaseAddress = new Uri($"http://{ipAddress}/json/"),
                Timeout = timeout
            };
            _ledCoordinates = coords.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Select(line => new ThreeDPoint(line.Trim().Trim('[', ']').Split(',').Select(s => double.Parse(s)).ToArray())).ToArray().AsReadOnly();
        }

        public async Task LoadStateAsync()
        {
            var state = await GetJsonStateAsync();
            var wledStatusJson = JsonNode.Parse(state)!;

            var segmentJson = wledStatusJson["seg"]![0]!; // TODO: Loop through every LED in every segment properly

            _ledsStart = (int)segmentJson["start"]!;
            _ledsEnd = (int)segmentJson["stop"]!;
        }

        public int LedCount => _ledsEnd - _ledsStart;
        public ReadOnlyCollection<ThreeDPoint> LedCoordinates => _ledCoordinates;

        public async Task<string> GetJsonStateAsync()
        {
            return await _client.GetStringAsync("state");
        }
        public async Task<string> SendCommand(dynamic commandObject)
        {
            var jsonCommand = JsonSerializer.Serialize(commandObject);
            var commandContent = new StringContent(jsonCommand, Encoding.UTF8, "application/json");
            commandContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var result = await _client.PostAsync(string.Empty, commandContent);
            var resultString = await result.Content.ReadAsStringAsync();
            result.EnsureSuccessStatusCode();

            return resultString;
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

        public async ValueTask DisposeAsync()
        {
            await SetLive(false);
            ((IDisposable)_client).Dispose();
        }
    }
}
