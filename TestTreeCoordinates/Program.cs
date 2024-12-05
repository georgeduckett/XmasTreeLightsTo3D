using System.Net.Http.Headers;
using System.Text;
using System;
using System.Text.Json.Nodes;
using System.Dynamic;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Net.Sockets;
using System.Net;
using WLEDInterface;

//var coords = File.ReadAllText("C:\\Users\\Lucy Duckett\\Source\\repos\\XmasTree\\XmasTree\\01_calibration\\coords_mattForm_adjusted.txt");
var coords = File.ReadAllText("C:\\Users\\georg\\source\\repos\\XmasTreeLightsTo3D\\Process3DImages\\bin\\Debug\\net8.0\\coordinates.csv");

await using var client = new WledTreeClient("192.168.0.70", TimeSpan.FromSeconds(10), coords);
Console.WriteLine("Querying status of WLED");
await client.LoadStateAsync();

await client.SetLive(false, 255);

await client.SetLive(true);

await client.SetLive(false);

Console.WriteLine("Checking whether Console.KeyAvailable will work (don't worry about an exception)");
var canQueryKeyAvailable = false;
try { var _ = Console.KeyAvailable; canQueryKeyAvailable = true; }
catch { }

var coordSelectors = new[] { new Func<ThreeDPoint, double>(c => c.Z), new Func<ThreeDPoint, double>(c => c.X), new Func<ThreeDPoint, double>(c => c.Y) };


try
{
    foreach (var coordSelector in coordSelectors)
    {
        client.SetAllLeds(Colours.Black);
        await client.ApplyUpdate();
        await Task.Delay(5000);

        var minZ = client.LedCoordinates.Select(coordSelector).Min();
        var maxZ = client.LedCoordinates.Select(coordSelector).Max();
        var delta = (maxZ - minZ) / 500;
        for (var z = minZ; z <= maxZ; z += delta)
        {
            if (canQueryKeyAvailable && Console.KeyAvailable) { break; }

            client.SetLedsColours(client.LedCoordinates.Select((c, i) => (c, i)).Where(c => coordSelector(c.c) >= z && coordSelector(c.c) <= z + delta).Select(c => new WledTreeClient.LedUpdate(c.i, Colours.White)).ToArray());
            await client.ApplyUpdate();
        }
    }
    // Convert all numbers to binary
    var binary = Enumerable.Range(client.LedIndexStart, client.LedIndexEnd - client.LedIndexStart)
        .Select(i => new string(Convert.ToString(i, 2).Reverse().ToArray())).ToArray();

    for (int reps = 0; reps < 1; reps++)
    {
        if (reps != 0)
        {
            await Task.Delay(5000);
        }

        for (var i = 0; i < binary.Max(index => index.Length); i++)
        {
            for (var ledIndex = client.LedIndexStart; ledIndex < client.LedIndexEnd; ledIndex++)
            {
                client.SetLedColour(ledIndex, i >= binary[ledIndex].Length || binary[ledIndex][i] == '0' ? Colours.Red : Colours.White); // Red means '0' as leds will start as off, so this is clearer
            }
            await client.ApplyUpdate();
            await Task.Delay(1000);
        }

        client.SetAllLeds(Colours.Black);
        await client.ApplyUpdate();
    }

    client.SetLedColour(287, Colours.Blue);
    await client.ApplyUpdate();
}
finally
{
    // Turn off live mode (for when using UDP)
    await client.SetLive(false);
}