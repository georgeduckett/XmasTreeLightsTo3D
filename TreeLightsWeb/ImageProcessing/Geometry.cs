namespace TreeLightsWeb.ImageProcessing
{
    /// <summary>A 3D point or direction (double precision).</summary>
    public readonly record struct Vec3(double X, double Y, double Z)
    {
        public static readonly Vec3 Zero = new(0, 0, 0);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(double s, Vec3 a) => new(s * a.X, s * a.Y, s * a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new(s * a.X, s * a.Y, s * a.Z);

        public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double DistanceTo(Vec3 b) => (this - b).Length;
        public Vec3 Normalised() => this * (1.0 / Length);
        public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + t * (b - a);
    }

    /// <summary>The small amount of dense linear algebra and statistics the reconstruction needs.</summary>
    public static class LinearAlgebra
    {
        /// <summary>Solves A x = b in place (A is n x n, row major) by Gaussian elimination with partial pivoting.
        /// Returns false if A is singular.</summary>
        public static bool Solve(double[] a, double[] b, int n)
        {
            for (var col = 0; col < n; col++)
            {
                var pivot = col;
                var best = Math.Abs(a[col * n + col]);
                for (var r = col + 1; r < n; r++)
                {
                    var v = Math.Abs(a[r * n + col]);
                    if (v > best) { best = v; pivot = r; }
                }
                if (best < 1e-300) return false;
                if (pivot != col)
                {
                    for (var c = 0; c < n; c++) (a[col * n + c], a[pivot * n + c]) = (a[pivot * n + c], a[col * n + c]);
                    (b[col], b[pivot]) = (b[pivot], b[col]);
                }
                var inv = 1.0 / a[col * n + col];
                for (var r = col + 1; r < n; r++)
                {
                    var factor = a[r * n + col] * inv;
                    if (factor == 0) continue;
                    for (var c = col; c < n; c++) a[r * n + c] -= factor * a[col * n + c];
                    b[r] -= factor * b[col];
                }
            }
            for (var r = n - 1; r >= 0; r--)
            {
                var s = b[r];
                for (var c = r + 1; c < n; c++) s -= a[r * n + c] * b[c];
                b[r] = s / a[r * n + r];
            }
            return true;
        }

        /// <summary>Inverts a 3x3 matrix (row major, 9 values) into <paramref name="inv"/>. Returns false if singular.</summary>
        public static bool Invert3(ReadOnlySpan<double> m, Span<double> inv)
        {
            var c00 = m[4] * m[8] - m[5] * m[7];
            var c01 = m[5] * m[6] - m[3] * m[8];
            var c02 = m[3] * m[7] - m[4] * m[6];
            var det = m[0] * c00 + m[1] * c01 + m[2] * c02;
            if (Math.Abs(det) < 1e-300) return false;
            var id = 1.0 / det;
            inv[0] = c00 * id; inv[1] = (m[2] * m[7] - m[1] * m[8]) * id; inv[2] = (m[1] * m[5] - m[2] * m[4]) * id;
            inv[3] = c01 * id; inv[4] = (m[0] * m[8] - m[2] * m[6]) * id; inv[5] = (m[2] * m[3] - m[0] * m[5]) * id;
            inv[6] = c02 * id; inv[7] = (m[1] * m[6] - m[0] * m[7]) * id; inv[8] = (m[0] * m[4] - m[1] * m[3]) * id;
            return true;
        }

        /// <summary>Weighted linear least squares (rows x cols design matrix, row major) via the normal equations.
        /// Returns null if it's underdetermined.</summary>
        public static double[]? LeastSquares(double[] design, double[] rhs, int rows, int cols, double[]? weights = null)
        {
            var ata = new double[cols * cols];
            var atb = new double[cols];
            for (var r = 0; r < rows; r++)
            {
                var w = weights?[r] ?? 1.0;
                if (w == 0) continue;
                for (var i = 0; i < cols; i++)
                {
                    var ai = design[r * cols + i] * w;
                    atb[i] += ai * rhs[r];
                    for (var j = 0; j < cols; j++) ata[i * cols + j] += ai * design[r * cols + j];
                }
            }
            return Solve(ata, atb, cols) ? atb : null;
        }

        /// <summary>The median (the mean of the middle two for an even count). Reorders <paramref name="values"/>.</summary>
        public static double Median(Span<double> values)
        {
            if (values.Length == 0) return double.NaN;
            values.Sort();
            var mid = values.Length / 2;
            return values.Length % 2 == 1 ? values[mid] : 0.5 * (values[mid - 1] + values[mid]);
        }

        public static double Median(double[] values) => Median(values.AsSpan());

        public static double Median(IEnumerable<double> values) => Median(values.ToArray().AsSpan());

        /// <summary>Linear-interpolated percentile (as numpy's default), p in [0, 100]. Reorders <paramref name="values"/>.</summary>
        public static double Percentile(Span<double> values, double p)
        {
            if (values.Length == 0) return double.NaN;
            values.Sort();
            var pos = (values.Length - 1) * p / 100.0;
            var lo = (int)Math.Floor(pos);
            var hi = Math.Min(lo + 1, values.Length - 1);
            return values[lo] + (pos - lo) * (values[hi] - values[lo]);
        }

        public static double Percentile(double[] values, double p) => Percentile(values.AsSpan(), p);

        public static double Percentile(IEnumerable<double> values, double p) => Percentile(values.ToArray().AsSpan(), p);
    }
}
