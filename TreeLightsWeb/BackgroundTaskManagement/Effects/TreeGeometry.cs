using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using WLEDInterface;

namespace TreeLightsWeb.BackgroundTaskManagement.Effects
{
    /// <summary>
    /// The LED positions, plus things most effects want that are worth working out once: each LED's distance from the
    /// trunk and angle round it, its height as a fraction of the tree's, the shape of the cone the LEDs are on, and (for
    /// effects that move from LED to LED) each LED's nearest neighbours.
    /// </summary>
    public sealed class TreeGeometry
    {
        private static readonly ConditionalWeakTable<ReadOnlyCollection<Vector3>, TreeGeometry> Cache = new();

        /// <summary>The geometry for the client's current coordinates (worked out once per set of coordinates).</summary>
        public static TreeGeometry For(WledTreeClient client) => Cache.GetValue(client.LedCoordinates, c => new TreeGeometry(c));

        private readonly Lazy<int[][]> _neighbours;

        public TreeGeometry(IReadOnlyList<Vector3> coordinates)
        {
            Count = coordinates.Count;
            X = new float[Count]; Y = new float[Count]; Z = new float[Count];
            R = new float[Count]; Theta = new float[Count]; Height = new float[Count];
            for (var i = 0; i < Count; i++)
            {
                X[i] = coordinates[i].X;
                Y[i] = coordinates[i].Y;
                Z[i] = coordinates[i].Z;
                R[i] = MathF.Sqrt(X[i] * X[i] + Y[i] * Y[i]);
                Theta[i] = MathF.Atan2(Y[i], X[i]);
            }
            ZMax = Count == 0 ? 1 : MathF.Max(0.01f, Z.Max());
            for (var i = 0; i < Count; i++) Height[i] = Z[i] / ZMax;

            // The cone the LEDs are wound round (through the middle of the layer of LEDs, which is what effects that follow
            // the tree's surface want): its radius at the bottom, from how far out the LEDs in the bottom quarter are
            var lowRadii = Enumerable.Range(0, Count).Where(i => Height[i] < 0.25f).Select(i => R[i]).OrderBy(r => r).ToArray();
            BaseRadius = lowRadii.Length == 0 ? 0.66f : 0.815f * lowRadii[(int)(0.9 * (lowRadii.Length - 1))];

            _neighbours = new Lazy<int[][]>(() => FindNeighbours(7));
        }

        public int Count { get; }
        public float[] X { get; }
        public float[] Y { get; }
        public float[] Z { get; }
        /// <summary>Distance from the trunk.</summary>
        public float[] R { get; }
        /// <summary>Angle round the trunk (radians, -π to π, anticlockwise from +x seen from above).</summary>
        public float[] Theta { get; }
        /// <summary>Height as a fraction of the tree's: 0 at the lowest LED, 1 at the top.</summary>
        public float[] Height { get; }
        public float ZMax { get; }
        /// <summary>The radius of the cone the LEDs are on (the middle of the layer of LEDs) at the bottom.</summary>
        public float BaseRadius { get; }

        /// <summary>The radius of the cone the LEDs are on at height <paramref name="z"/>.</summary>
        public float ConeRadius(float z) => MathF.Max(0.1f, BaseRadius * (1 - z / (ZMax * 1.06f)));

        /// <summary>Each LED's 7 nearest LEDs, nearest first.</summary>
        public int[][] Neighbours => _neighbours.Value;

        public float DistanceSquared(int i, float x, float y, float z)
        {
            var dx = X[i] - x; var dy = Y[i] - y; var dz = Z[i] - z;
            return dx * dx + dy * dy + dz * dz;
        }

        private int[][] FindNeighbours(int k)
        {
            var result = new int[Count][];
            Parallel.For(0, Count, i =>
            {
                var best = new List<(float D, int J)>(k + 1);
                for (var j = 0; j < Count; j++)
                {
                    if (j == i) continue;
                    var d = DistanceSquared(j, X[i], Y[i], Z[i]);
                    if (best.Count < k || d < best[^1].D)
                    {
                        var at = best.FindIndex(b => b.D > d);
                        best.Insert(at < 0 ? best.Count : at, (d, j));
                        if (best.Count > k) best.RemoveAt(k);
                    }
                }
                result[i] = best.Select(b => b.J).ToArray();
            });
            return result;
        }
    }
}
