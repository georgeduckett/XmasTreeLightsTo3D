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
using System.Numerics;

//var coords = File.ReadAllText("C:\\Users\\Lucy Duckett\\Source\\repos\\XmasTree\\XmasTree\\01_calibration\\coords_mattForm_adjusted.txt");
var coords = File.ReadAllText("C:\\Users\\georg\\source\\repos\\XmasTreeLightsTo3D\\Process3DImages\\bin\\Debug\\net8.0\\coordinates.csv");

await using var client = new WledTreeClient("192.168.0.70", TimeSpan.FromSeconds(10), coords);
Console.WriteLine("Querying status of WLED");
await client.LoadStateAsync();

// Turn it on, at full brightness
await client.SetOnOff(true, 255);

var canQueryKeyAvailable = false;
try { var _ = Console.KeyAvailable; canQueryKeyAvailable = true; }
catch { Console.Clear(); }


Func<float, Quaternion>[] rotationDirections = [angle => Quaternion.CreateFromYawPitchRoll(angle, 0, 0), angle => Quaternion.CreateFromYawPitchRoll(0, angle, 0), angle => Quaternion.CreateFromYawPitchRoll(0, 0, angle)];
foreach (var rotationDir in rotationDirections)
{
    Console.WriteLine("Set all to black");
    client.SetAllLeds(Colours.Black);
    await client.ApplyUpdate();
    var rotationAngle = 0.0f;
    while (rotationAngle < 300)
    {
        if (canQueryKeyAvailable && Console.KeyAvailable) { while (Console.KeyAvailable) { Console.ReadKey(); } break; }

        Console.Write("\rRotationAngle: " + rotationAngle.ToString());

        client.SetLedsColours(c => Vector3.Transform(c - new Vector3(0, 0, client.LedCoordinates.Max(c => c.Z) / 2), rotationDir(rotationAngle)).X >= 0 ? Colours.Red : Colours.Green);
        await client.ApplyUpdate();
        rotationAngle += (float)(180 / Math.PI) / 500;
    }
}

await Task.Delay(5000);
Console.WriteLine("Sweep a plane in each direction");
var coordSelectors = new[] { new Func<Vector3, double>(c => c.Z), new Func<Vector3, double>(c => c.X), new Func<Vector3, double>(c => c.Y) };
foreach (var coordSelector in coordSelectors)
{
    Console.WriteLine("Set all to black");
    client.SetAllLeds(Colours.Black);
    await client.ApplyUpdate();
    await Task.Delay(5000);

    var minZ = client.LedCoordinates.Select(coordSelector).Min();
    var maxZ = client.LedCoordinates.Select(coordSelector).Max();
    var delta = (maxZ - minZ) / 500;
    for (var z = minZ; z <= maxZ; z += delta)
    {
        Console.Write($"\rSweeping coordinates, {(z - minZ) / (maxZ - minZ):P0}");
        if (canQueryKeyAvailable && Console.KeyAvailable) { while (Console.KeyAvailable) { Console.ReadKey(); } break; }

        client.SetLedsColours(c => coordSelector(c) <= z + delta ? Colours.White : Colours.Black);
        await client.ApplyUpdate();
    }
    Console.WriteLine();
}

Console.WriteLine("Flashing indexes in binary");
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
        if (canQueryKeyAvailable && Console.KeyAvailable) { while (Console.KeyAvailable) { Console.ReadKey(); } break; }

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