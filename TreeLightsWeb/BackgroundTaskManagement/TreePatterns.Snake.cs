﻿using System.Numerics;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public static partial class TreePatterns
    {
        public static async ValueTask Snake(WledTreeClient client, CancellationToken cancellationToken)
        {
            client.SetAllLeds(Colours.Black);
            await client.ApplyUpdate();

            // Get the (unnormalised) direction from i to j for each point.
            var directions = new Dictionary<int, Dictionary<int, Vector3>>();
            for (var i = client.LedIndexStart; i < client.LedIndexEnd; i++)
            {
                var ledDistances = new Dictionary<int, Vector3>();
                for (var j = client.LedIndexStart + 1; j < client.LedIndexEnd; j++)
                {
                    if (i != j)
                    {
                        // We do it both ways round to sacrifice memory for speed
                        ledDistances[j] = client.LedCoordinates[j] - client.LedCoordinates[i];
                    }
                }

                var orderedDistances = ledDistances.OrderBy(kv => kv.Value.Length()).ToArray();

                // We need to try to have not just the closest 5, but 5 that are in different directions, while also being close
                // For each led, keep adding the closest led that isn't in the same direction as the previous ones
                var bestConnections = new Dictionary<int, Vector3>
                    {
                        { orderedDistances[0].Key, orderedDistances[0].Value }
                    };
                for (var j = 1; j < orderedDistances.Length; j++)
                {
                    if (bestConnections.Count == 6) { break; }

                    var isGood = true;
                    foreach (var connection in bestConnections)
                    {
                        if (Math.Abs(Math.Acos(Vector3.Dot(Vector3.Normalize(connection.Value), Vector3.Normalize(orderedDistances[j].Value)))) < Math.PI / 4)
                        { // we found a connection that is in the same direction as one of the ones we already have so don't use it
                            isGood = false;
                            break;
                        }
                    }

                    if (isGood)
                    {
                        bestConnections.Add(orderedDistances[j].Key, orderedDistances[j].Value);
                    }
                }

                directions[i] = bestConnections;
            }


            // Now that we've got the best connections for each index normalize the vectors to make things easier later
            foreach (var direction in directions)
            {
                foreach (var connection in direction.Value)
                {
                    directions[direction.Key][connection.Key] = Vector3.Normalize(connection.Value);
                }
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var freeLeds = new HashSet<int>(Enumerable.Range(client.LedIndexStart, client.LedIndexEnd - client.LedIndexStart));
                var snakeLength = 2;
                var snakeLeds = new List<int> { Random.Shared.Next(client.LedIndexStart, client.LedIndexEnd) };
                freeLeds.RemoveWhere(i => snakeLeds.Contains(i));

                var appleLocation = freeLeds.RandomElement(Random.Shared);

                client.SetLedsColours(snakeLeds.Select(i => new WledTreeClient.LedUpdate(i, Colours.White)).ToArray());
                client.SetLedColour(appleLocation, Colours.Red);
                await client.ApplyUpdate();
                await DelayNoException(100, cancellationToken);
                var iterationsWithNoApple = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Decide where to move the snake next
                    var possibleMoves = directions[snakeLeds[0]].Where(kv => freeLeds.Contains(kv.Key)).ToArray();

                    if (possibleMoves.Length == 0)
                    { // Snake has no where to go so we're done moving the snake
                        break;
                    }
                    else
                    {
                        // Get the 'best' possible move based on moving in the direction of the apple
                        var snakeToApple = Vector3.Normalize(client.LedCoordinates[appleLocation] - client.LedCoordinates[snakeLeds[0]]);
                        var bestMove = possibleMoves.OrderBy(kv => Math.Abs(Math.Acos(Vector3.Dot(kv.Value, snakeToApple)))).First();

                        client.SetLedColour(snakeLeds[0], Colours.Green);
                        snakeLeds.Insert(0, bestMove.Key);
                        freeLeds.Remove(bestMove.Key);
                        client.SetLedColour(snakeLeds[0], Colours.White);
                        for (int i = 1; i < snakeLeds.Count; i++)
                        {
                            client.SetLedColour(snakeLeds[i], new RGBValue(0, (byte)Math.Ceiling((1 - (i / Math.Max(10.0, snakeLeds.Count - 1))) * (255 - 60) + 60), 0));
                        }

                        if (bestMove.Key == appleLocation)
                        {
                            iterationsWithNoApple = 0;
                            snakeLength++;
                            appleLocation = freeLeds.RandomElement(Random.Shared);
                            client.SetLedColour(appleLocation, Colours.Red);
                        }
                        else if (iterationsWithNoApple > 250)
                        {
                            // We keep moving apples and still can't get them, so just break out of the loop and start again
                            break;
                        }
                        else if (iterationsWithNoApple++ == 50)
                        { // We've gone too long without an apple so just change the location randomly
                            client.SetLedColour(appleLocation, Colours.Black);
                            appleLocation = freeLeds.RandomElement(Random.Shared);
                            client.SetLedColour(appleLocation, Colours.Red);
                        }

                        if (snakeLeds.Count > snakeLength)
                        {
                            client.SetLedColour(snakeLeds.Last(), Colours.Black);
                            freeLeds.Add(snakeLeds[snakeLeds.Count - 1]);
                            snakeLeds.RemoveAt(snakeLeds.Count - 1);
                        }
                    }

                    await DelayNoException(180, cancellationToken);
                    await client.ApplyUpdate();
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    // Clear the board
                    foreach (var snakeLEDIndex in snakeLeds)
                    {
                        await DelayNoException(150, cancellationToken);
                        client.SetLedColour(snakeLEDIndex, Colours.Black);
                        await client.ApplyUpdate();
                    }
                    // Remove the apple
                    await DelayNoException(1000, cancellationToken);
                    client.SetLedColour(appleLocation, Colours.Black);
                    await client.ApplyUpdate();
                }


                // Wait to restart
                await DelayNoException(3000, cancellationToken);
            }
        }
    }
}
