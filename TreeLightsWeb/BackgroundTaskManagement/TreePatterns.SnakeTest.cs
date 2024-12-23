using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using TreeLightsWeb.Extensions;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement
{
    public partial class TreePatterns
    {
        private class Path<TNode> : IEnumerable<TNode>
        {
            public TNode LastStep { get; private set; }
            public Path<TNode>? PreviousSteps { get; private set; }
            public double TotalCost { get; private set; }
            private Path(TNode lastStep, Path<TNode>? previousSteps, double totalCost)
            {
                LastStep = lastStep;
                PreviousSteps = previousSteps;
                TotalCost = totalCost;
            }
            public Path(TNode start) : this(start, null, 0) { }
            public Path<TNode> AddStep(TNode step, double stepCost)
            {
                return new Path<TNode>(step, this, TotalCost + stepCost);
            }
            public IEnumerator<TNode> GetEnumerator()
            {
                for (Path<TNode>? p = this; p != null; p = p.PreviousSteps)
                    yield return p.LastStep;
            }
            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
        private class PriorityQueue<TPriority, TValue> where TPriority : notnull
        {
            private readonly SortedDictionary<TPriority, Queue<TValue>> list = [];
            public void Enqueue(TPriority priority, TValue value)
            {
                Queue<TValue>? q;
                if (!list.TryGetValue(priority, out q))
                {
                    q = new Queue<TValue>();
                    list.Add(priority, q);
                }
                q.Enqueue(value);
            }
            public TValue Dequeue()
            {
                // will throw if there isn’t any first element!
                var pair = list.First();
                var v = pair.Value.Dequeue();
                if (pair.Value.Count == 0) // nothing left of the top priority.
                    list.Remove(pair.Key);
                return v;
            }
            public bool IsEmpty
            {
                get { return !list.Any(); }
            }
        }
        private static class AStar
        {
            public static Path<Node>? FindPath<Node>(
                Node start,
                Node destination,
                Func<Node, Node, double> distance,
                Func<Node, double> estimate,
                Func<Node, IEnumerable<Node>> neighbours)
            {
                var closed = new HashSet<Node>();
                var queue = new PriorityQueue<double, Path<Node>>();
                queue.Enqueue(0, new Path<Node>(start));
                while (!queue.IsEmpty)
                {
                    var path = queue.Dequeue();
                    if (closed.Contains(path.LastStep))
                        continue;
                    if (path.LastStep!.Equals(destination))
                        return path;
                    closed.Add(path.LastStep);
                    foreach (var n in neighbours(path.LastStep))
                    {
                        var d = distance(path.LastStep, n);
                        if (n!.Equals(destination))
                            d = 0;
                        var newPath = path.AddStep(n, d);
                        queue.Enqueue(newPath.TotalCost + estimate(n), newPath);
                    }
                }
                return null;
            }
        }

        public async ValueTask SnakeTest(WledTreeClient client, CancellationToken cancellationToken)
        {
            client.SetAllLeds(Colours.Black);
            await ApplyUpdate(client, cancellationToken, delayAfterMS: 1000);

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

            while (!cancellationToken.IsCancellationRequested)
            {
                var freeLeds = new HashSet<int>(Enumerable.Range(client.LedIndexStart, client.LedIndexEnd - client.LedIndexStart));
                var snakeLength = 2;
                var snakeLeds = new List<int> { Random.Shared.Next(client.LedIndexStart, client.LedIndexEnd) };
                freeLeds.RemoveWhere(snakeLeds.Contains);

                var appleLocation = freeLeds.RandomElement(Random.Shared);

                client.SetLedsColours(snakeLeds.Select(i => new WledTreeClient.LedUpdate(i, Colours.White)).ToArray());
                client.SetLedColour(appleLocation, Colours.Red);
                await ApplyUpdate(client, cancellationToken, delayAfterMS: 100);
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
                        var bestPath = AStar.FindPath(snakeLeds[0],
                                                      appleLocation,
                                                      (a, b) => (client.LedCoordinates[a] - client.LedCoordinates[b]).Length(),
                                                      a => (client.LedCoordinates[a] - client.LedCoordinates[appleLocation]).Length(),
                                                      n => directions[n].Keys)?.Reverse()?.Skip(1).ToQueue();

                        // If A+ fails, just move in the direction of the apple
                        if (bestPath == null || bestPath.Count == 0)
                        {
                            // Get the 'best' possible move based on moving in the direction of the apple
                            var snakeToApple = Vector3.Normalize(client.LedCoordinates[appleLocation] - client.LedCoordinates[snakeLeds[0]]);
                            bestPath = new Queue<int>([snakeLeds[0], possibleMoves.OrderBy(kv => Math.Abs(Math.Acos(Vector3.Dot(kv.Value, snakeToApple)))).First().Key]);
                        }

                        // Travel down the A+ path until we hit the apple
                        while (!cancellationToken.IsCancellationRequested && bestPath.Count > 0)
                        {
                            var bestMove = bestPath.Dequeue();

                            client.SetLedColour(snakeLeds[0], Colours.Green);
                            snakeLeds.Insert(0, bestMove);
                            freeLeds.Remove(bestMove);
                            client.SetLedColour(snakeLeds[0], Colours.White);
                            for (int i = 1; i < snakeLeds.Count; i++)
                            {
                                client.SetLedColour(snakeLeds[i], new RGBValue(0, (byte)Math.Ceiling((1 - (i / Math.Max(10.0, snakeLeds.Count - 1))) * (255 - 60) + 60), 0));
                            }

                            if (bestMove == appleLocation)
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

                            // If the snake is too long blank the last snake led and remove it
                            if (snakeLeds.Count > snakeLength)
                            {
                                client.SetLedColour(snakeLeds.Last(), Colours.Black);
                                freeLeds.Add(snakeLeds[snakeLeds.Count - 1]);
                                snakeLeds.RemoveAt(snakeLeds.Count - 1);
                            }
                            await ApplyUpdate(client, cancellationToken, delayAfterMS: 180);
                        }
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    // Clear the board
                    foreach (var snakeLEDIndex in snakeLeds)
                    {
                        client.SetLedColour(snakeLEDIndex, Colours.Black);
                        await ApplyUpdate(client, cancellationToken, delayAfterMS: 150);
                    }
                    // Remove the apple
                    client.SetLedColour(appleLocation, Colours.Black);
                    await ApplyUpdate(client, cancellationToken, delayBeforeMS: 1000, delayAfterMS: 3000);
                }
            }
        }
    }
}