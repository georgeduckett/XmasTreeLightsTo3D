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

var coords = File.ReadAllText("C:\\Users\\Lucy Duckett\\Source\\repos\\XmasTree\\XmasTree\\01_calibration\\coords_mattForm_adjusted.txt");

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

var colourSetObject = (dynamic)new ExpandoObject();
colourSetObject.seg = (dynamic)new ExpandoObject();


var coordSelector = new Func<ThreeDPoint, double>(c => c.Z);

var minZ = client.LedCoordinates.Select(coordSelector).Min();
var maxZ = client.LedCoordinates.Select(coordSelector).Max();

try
{
    var delta = 10;
    for (var z = minZ;z <= maxZ;z+= delta)
    {
        if (canQueryKeyAvailable && Console.KeyAvailable) { break; }

        colourSetObject.seg.i = client.LedCoordinates.Select((c, i) => (c, i)).Where(c => coordSelector(c.c) >= z && coordSelector(c.c) <= z + delta).SelectMany(c => new object[] { c.i, "FFFFFF" }).ToArray();
        await client.SendCommand(colourSetObject);
    }
}
finally
{
    // Turn off live mode (for when using UDP)
    await client.SetLive(false);
}