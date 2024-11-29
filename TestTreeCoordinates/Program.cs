using System.Net.Http.Headers;
using System.Text;
using System;
using System.Text.Json.Nodes;
using System.Dynamic;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Net.Sockets;
using System.Net;

var coords = File.ReadAllText("C:\\Users\\Lucy Duckett\\Source\\repos\\XmasTree\\XmasTree\\01_calibration\\coords_mattForm_adjusted.txt");

static async Task<string> SendCommand(HttpClient client, dynamic commandObject)
{
    var jsonCommand = JsonSerializer.Serialize(commandObject);
    var commandContent = new StringContent(jsonCommand, Encoding.UTF8, "application/json");
    commandContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
    var result = await client.PostAsync("http://192.168.0.70/json", commandContent);
    var resultString = await result.Content.ReadAsStringAsync();
    result.EnsureSuccessStatusCode();

    return resultString;
}

using var client = new HttpClient();
client.Timeout = TimeSpan.FromSeconds(10);
Console.WriteLine("Querying status of WLED");
var wledstatus = await client.GetStringAsync("http://192.168.0.70/json/state");

var wledStatusJson = JsonNode.Parse(wledstatus)!;

var segmentJson = wledStatusJson["seg"]![0]!; // TODO: Loop through every LED in every segment properly

var start = (int)segmentJson["start"]!;
var end = (int)segmentJson["stop"]!;

var oldBrightness = (byte)wledStatusJson["bri"]!;

var liveObject = (dynamic)new ExpandoObject();
liveObject.live = false;
liveObject.bri = (byte)255; // Set brightness to full since that seems to impact what full on is when live
await SendCommand(client, liveObject); // Turn live off just in case it's still on...

liveObject.live = true;
await SendCommand(client, liveObject); // So when we turn it on it blanks all the leds

liveObject.live = false; // Not using live for JSON (we do for UDP), but turn it on and off to blank leds
await SendCommand(client, liveObject); // So when we turn it on it blanks all the leds

Console.WriteLine("Checking whether Console.KeyAvailable will work (don't worry about an exception)");
var canQueryKeyAvailable = false;
try { var _ = Console.KeyAvailable; canQueryKeyAvailable = true; }
catch { }
// Connect to WLED via UDP for realtime LED changes
//using var udpClient = new UdpClient();
//udpClient.Connect(IPEndPoint.Parse("192.168.0.70:21324"));
//liveObject.live = true;
//await SendCommand(client, liveObject); // So when we turn it on it blanks all the leds


var udpBytes = new byte[10]; // Enough for 2 LEDs (4 for header, then 3 per led)
udpBytes[0] = 4; // Protocol 4, DNRGB
udpBytes[1] = 2; // Wait 2 seconds before exiting realtime mode having not received any more commands
// Next two bytes are for first LED index
udpBytes[4] = 255;
udpBytes[5] = 255;
udpBytes[6] = 255;
udpBytes[7] = 0;
udpBytes[8] = 0;
udpBytes[9] = 0;


var colourSetObject = (dynamic)new ExpandoObject();
colourSetObject.seg = (dynamic)new ExpandoObject();


var fileText = coords; // TODO: Read from the produced file

var coordinates = fileText.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Select(line => new ThreeDPoint(line.Trim().Trim('[', ']').Split(',').Select(s => double.Parse(s)).ToArray())).ToArray();


var coordSelector = new Func<ThreeDPoint, double>(c => c.Z);

var minZ = coordinates.Select(coordSelector).Min();
var maxZ = coordinates.Select(coordSelector).Max();

try
{
    var delta = 10;
    for (var z = minZ;z <= maxZ;z+= delta)
    {
        if (canQueryKeyAvailable && Console.KeyAvailable) { break; }

        colourSetObject.seg.i = coordinates.Select((c, i) => (c, i)).Where(c => coordSelector(c.c) >= z && coordSelector(c.c) <= z + delta).SelectMany(c => new object[] { c.i, "FFFFFF" }).ToArray();
        await SendCommand(client, colourSetObject);
    }
}
finally
{
    // Turn off live mode (for when using UDP) and restore old brightness 
    liveObject.live = false;
    liveObject.bri = oldBrightness;
    await SendCommand(client, liveObject);
}
record ThreeDPoint(double X, double Y, double Z)
{
    public ThreeDPoint(double[] points) : this(points[0], points[1], points[2]) { }
}