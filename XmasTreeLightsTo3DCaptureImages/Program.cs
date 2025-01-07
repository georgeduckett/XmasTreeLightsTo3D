using System.Net.Http.Headers;
using System.Text;
using System;
using System.Text.Json.Nodes;
using System.Dynamic;
using System.Text.Json.Serialization;
using System.Text.Json;
using OpenCvSharp;
using System.Net.Sockets;
using System.Net;
static async Task<string> SendCommand(HttpClient client, dynamic commandObject)
{
    for (var i = 0; i<10; i++)
    {
        try
        {
            var jsonCommand = JsonSerializer.Serialize(commandObject);
            var commandContent = new StringContent(jsonCommand, Encoding.UTF8, "application/json");
            commandContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var result = await client.PostAsync("http://192.168.0.70/json", commandContent);
            var resultString = await result.Content.ReadAsStringAsync();
            result.EnsureSuccessStatusCode();

            return resultString;
        }
    catch { Console.WriteLine($"Timeout, retrying {i+1} of 10"); }
    }
    throw new Exception("Timed out too many times");
}

// 0, 45, 90, 135, 180, 225, 270 & 315
var direction = 0;

/*foreach (var file in Directory.GetFiles(Directory.GetCurrentDirectory(), "*.png"))
{
    File.Delete(file);
}*/

using var client = new HttpClient();
client.Timeout = TimeSpan.FromSeconds(10);
Console.WriteLine("Querying status of WLED");
var wledstatus = await client.GetStringAsync("http://192.168.0.70/json/state");

var wledStatusJson = JsonNode.Parse(wledstatus)!;

var segmentJson = wledStatusJson["seg"]![0]!; // TODO: Loop through every LED in every segment properly

var start = (int)segmentJson["start"]!;
var end = (int)segmentJson["stop"]!;

// Get the camera ready to capture
using var vc = new VideoCapture(0);
using var frame = new Mat();
vc.Open(0);

var oldBrightness = (byte)wledStatusJson["bri"]!;

var liveObject = (dynamic)new ExpandoObject();
liveObject.live = false;
liveObject.bri = (byte)255; // Set brightness to full since that seems to impact what full on is when live
await SendCommand(client, liveObject); // Turn live off just in case it's still on...

liveObject.live = true;
await SendCommand(client, liveObject); // So when we turn it on it blanks all the leds

liveObject.live = false; // Not using live for JSON (we do for UDP), but turn it on and off to blank leds
await SendCommand(client, liveObject); // So when we turn it on it blanks all the leds


// Save an image with all the LEDs off
vc.Read(frame);
frame.ImWrite($"_{direction}.png");


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
try
{
    for (var i = end - 1; i >= start; i--)
    { // Go backwards as we clear the later indexed led
        Console.Write($"\r{(!canQueryKeyAvailable ? "\n" : "")}Processing {end} to {start}; {i}");
        if (canQueryKeyAvailable && Console.KeyAvailable) { break; }

        udpBytes[2] = (byte)(i >> 8);
        udpBytes[3] = (byte)i;


        colourSetObject.seg.i = new object[] { i, "FFFFFF", i + 1, "000000" };
        await SendCommand(client, colourSetObject);
        
        // Don't use UDP as unreliable
        /*if (i == end - 1)
        { // If at the end don't need to blank the next indexed one (the last one we just lit up)
            await udpClient.SendAsync(udpBytes, 4 + 3);
        }
        else
        {
            await udpClient.SendAsync(udpBytes, 4 + (3 * 2));
        }*/

        // Time for the LED to physically react before taking a picture
        Thread.Sleep(200);

        //Save the image
        vc.Read(frame);
        frame.ImWrite($"{i}_{direction}.png");
    }

    //udpClient.Close();
    vc.Release();
}
finally
{
    // Turn off live mode (for when using UDP) and restore old brightness 
    liveObject.live = false;
    liveObject.bri = oldBrightness;
    await SendCommand(client, liveObject);
}