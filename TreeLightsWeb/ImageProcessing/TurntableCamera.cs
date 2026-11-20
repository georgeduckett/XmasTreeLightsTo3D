namespace TreeLightsWeb.ImageProcessing
{
    /// <summary>
    /// The geometry of the capture: a fixed camera looking at a tree that's turned about its own vertical axis between
    /// views.
    ///
    /// Tree coordinates: z is the axis the tree is turned about (the trunk), z = 0 at the camera's height, and the unit of
    /// length is the distance from the camera to that axis. For view k the tree has been turned by Sign * alpha_k about the
    /// axis (alpha_0 = 0) and may also have shifted sideways a little (tx_k, ty_k) as it was turned by hand. The camera
    /// sits at (0, -1, 0) looking roughly along +y, with a focal length f (in pixels; the principal point is the image
    /// centre) and a small yaw, pitch and roll.
    ///
    /// Parameters: [f, yaw, pitch, roll, alpha_1..alpha_{V-1}, tx_1..tx_{V-1}, ty_1..ty_{V-1}].
    /// </summary>
    public sealed class TurntableCamera
    {
        // Rotation (row major) from the turned tree's frame (x, -z, y + 1) to the camera's, and its derivatives
        private readonly double[] _r = new double[9];
        private readonly double[] _dYaw = new double[9];
        private readonly double[] _dPitch = new double[9];
        private readonly double[] _dRoll = new double[9];
        private readonly double[] _cos, _sin, _tx, _ty;
        private readonly double[] _parameters;

        public TurntableCamera(int views, int width, int height, int sign, double[] parameters)
        {
            if (parameters.Length != ParameterCount(views)) throw new ArgumentException("Wrong number of camera parameters", nameof(parameters));
            Views = views;
            Width = width;
            Height = height;
            Sign = sign;
            _parameters = (double[])parameters.Clone();

            _cos = new double[views]; _sin = new double[views]; _tx = new double[views]; _ty = new double[views];
            for (var k = 0; k < views; k++)
            {
                var a = sign * Angle(k);
                _cos[k] = Math.Cos(a);
                _sin[k] = Math.Sin(a);
                _tx[k] = k == 0 ? 0 : _parameters[IX + k - 1];
                _ty[k] = k == 0 ? 0 : _parameters[IY + k - 1];
            }

            double cy = Math.Cos(Yaw), sy = Math.Sin(Yaw), cp = Math.Cos(Pitch), sp = Math.Sin(Pitch), cr = Math.Cos(Roll), sr = Math.Sin(Roll);
            double[] ry = [cy, 0, sy, 0, 1, 0, -sy, 0, cy], dry = [-sy, 0, cy, 0, 0, 0, -cy, 0, -sy];
            double[] rx = [1, 0, 0, 0, cp, -sp, 0, sp, cp], drx = [0, 0, 0, 0, -sp, -cp, 0, cp, -sp];
            double[] rz = [cr, -sr, 0, sr, cr, 0, 0, 0, 1], drz = [-sr, -cr, 0, cr, -sr, 0, 0, 0, 0];
            Mul(Mul(rz, rx), ry, _r);
            Mul(Mul(rz, rx), dry, _dYaw);
            Mul(Mul(rz, drx), ry, _dPitch);
            Mul(Mul(drz, rx), ry, _dRoll);
        }

        public static int ParameterCount(int views) => 4 + 3 * (views - 1);

        /// <summary>Starting parameters: tree turned by the given angles, no shifts, no pitch or roll.</summary>
        public static double[] InitialParameters(int views, double focalLength, double yaw, IReadOnlyList<double> angles)
        {
            var g = new double[ParameterCount(views)];
            g[0] = focalLength;
            g[1] = yaw;
            for (var k = 1; k < views; k++) g[4 + k - 1] = angles[k];
            return g;
        }

        public int Views { get; }
        public int Width { get; }
        public int Height { get; }
        /// <summary>Which way the tree was turned (+1 or -1).</summary>
        public int Sign { get; }
        public int ParameterCountValue => _parameters.Length;
        public IReadOnlyList<double> Parameters => _parameters;
        public double[] CopyParameters() => (double[])_parameters.Clone();

        // Where the per view parameters start
        public const int IA = 4;
        public int IX => 4 + Views - 1;
        public int IY => 4 + 2 * (Views - 1);

        public double FocalLength => _parameters[0];
        public double Yaw => _parameters[1];
        public double Pitch => _parameters[2];
        public double Roll => _parameters[3];
        public double Angle(int k) => k == 0 ? 0 : _parameters[IA + k - 1];
        public double ShiftX(int k) => _tx[k];
        public double ShiftY(int k) => _ty[k];

        public TurntableCamera With(double[] parameters) => new(Views, Width, Height, Sign, parameters);

        /// <summary>The index into the parameters of each of the (up to) 7 parameters an observation in view k depends on:
        /// f, yaw, pitch, roll, alpha_k, tx_k, ty_k (-1 for view 0's, which are fixed).</summary>
        public void ParameterIndices(int k, Span<int> indices)
        {
            indices[0] = 0; indices[1] = 1; indices[2] = 2; indices[3] = 3;
            indices[4] = k == 0 ? -1 : IA + k - 1;
            indices[5] = k == 0 ? -1 : IX + k - 1;
            indices[6] = k == 0 ? -1 : IY + k - 1;
        }

        public void Project(in Vec3 p, int k, out double u, out double v)
        {
            double c = _cos[k], s = _sin[k];
            var x = c * p.X - s * p.Y + _tx[k];
            var y = s * p.X + c * p.Y + _ty[k];
            double q0x = x, q0y = -p.Z, q0z = y + 1.0;
            var r = _r;
            var qx = r[0] * q0x + r[1] * q0y + r[2] * q0z;
            var qy = r[3] * q0x + r[4] * q0y + r[5] * q0z;
            var qz = r[6] * q0x + r[7] * q0y + r[8] * q0z;
            var f = _parameters[0];
            u = 0.5 * Width + f * qx / qz;
            v = 0.5 * Height + f * qy / qz;
        }

        /// <summary>Projects a point and gives the derivatives of (u, v): <paramref name="jp"/> (6 values: du/dP then dv/dP)
        /// and <paramref name="jg"/> (14 values: du then dv with respect to the 7 parameters listed by
        /// <see cref="ParameterIndices"/>). Returns false if the point is behind the camera.</summary>
        public bool Project(in Vec3 p, int k, out double u, out double v, Span<double> jp, Span<double> jg)
        {
            double c = _cos[k], s = _sin[k];
            var xr = c * p.X - s * p.Y;
            var yr = s * p.X + c * p.Y;
            double q0x = xr + _tx[k], q0y = -p.Z, q0z = yr + _ty[k] + 1.0;
            var r = _r;
            var qx = r[0] * q0x + r[1] * q0y + r[2] * q0z;
            var qy = r[3] * q0x + r[4] * q0y + r[5] * q0z;
            var qz = r[6] * q0x + r[7] * q0y + r[8] * q0z;
            var f = _parameters[0];
            var iz = 1.0 / qz;
            var a = qx * iz;
            var b = qy * iz;
            u = 0.5 * Width + f * a;
            v = 0.5 * Height + f * b;
            var fiz = f * iz;

            // d(u,v)/dw for a change w in camera coordinates: f/qz * (w0 - a w2, w1 - b w2)
            // dq/dP = R * [[c, -s, 0], [0, 0, -1], [s, c, 0]]
            for (var i = 0; i < 3; i++)
            {
                double m0, m1, m2; // column i of R * Dq0P
                switch (i)
                {
                    case 0: m0 = c * r[0] + s * r[2]; m1 = c * r[3] + s * r[5]; m2 = c * r[6] + s * r[8]; break;
                    case 1: m0 = -s * r[0] + c * r[2]; m1 = -s * r[3] + c * r[5]; m2 = -s * r[6] + c * r[8]; break;
                    default: m0 = -r[1]; m1 = -r[4]; m2 = -r[7]; break;
                }
                jp[i] = fiz * (m0 - a * m2);
                jp[3 + i] = fiz * (m1 - b * m2);
            }

            jg[0] = a; jg[7] = b; // focal length
            Dq(_dYaw, q0x, q0y, q0z, fiz, a, b, jg, 1);
            Dq(_dPitch, q0x, q0y, q0z, fiz, a, b, jg, 2);
            Dq(_dRoll, q0x, q0y, q0z, fiz, a, b, jg, 3);
            if (k == 0)
            {
                jg[4] = jg[5] = jg[6] = jg[11] = jg[12] = jg[13] = 0;
            }
            else
            {
                // alpha: dq0 = Sign * (-yr, 0, xr); tx: dq0 = (1, 0, 0); ty: dq0 = (0, 0, 1)
                Dq(r, -Sign * yr, 0, Sign * xr, fiz, a, b, jg, 4);
                jg[5] = fiz * (r[0] - a * r[6]); jg[12] = fiz * (r[3] - b * r[6]);
                jg[6] = fiz * (r[2] - a * r[8]); jg[13] = fiz * (r[5] - b * r[8]);
            }
            return qz > 0;
        }

        private static void Dq(double[] m, double x, double y, double z, double fiz, double a, double b, Span<double> jg, int col)
        {
            var w0 = m[0] * x + m[1] * y + m[2] * z;
            var w1 = m[3] * x + m[4] * y + m[5] * z;
            var w2 = m[6] * x + m[7] * y + m[8] * z;
            jg[col] = fiz * (w0 - a * w2);
            jg[7 + col] = fiz * (w1 - b * w2);
        }

        /// <summary>The ray (in tree coordinates) through pixel (u, v) of view k: the camera centre and a unit direction.</summary>
        public (Vec3 Centre, Vec3 Direction) Ray(double u, double v, int k)
        {
            var f = _parameters[0];
            double dx = (u - 0.5 * Width) / f, dy = (v - 0.5 * Height) / f, dz = 1.0;
            var r = _r; // R^T * dc
            var d0x = r[0] * dx + r[3] * dy + r[6] * dz;
            var d0y = r[1] * dx + r[4] * dy + r[7] * dz;
            var d0z = r[2] * dx + r[5] * dy + r[8] * dz;
            var dir = new Vec3(d0x, d0z, -d0y).Normalised();
            double c = _cos[k], s = _sin[k]; // undo the turn: rotate by -Sign * alpha_k
            Vec3 Unturn(Vec3 w) => new(c * w.X + s * w.Y, -s * w.X + c * w.Y, w.Z);
            return (Unturn(new Vec3(-_tx[k], -1.0 - _ty[k], 0)), Unturn(dir));
        }

        /// <summary>Midpoint of the closest approach of two rays, or null if they're (nearly) parallel or it's not in front
        /// of both cameras.</summary>
        public static Vec3? Triangulate(in Vec3 c1, in Vec3 d1, in Vec3 c2, in Vec3 d2)
        {
            var w = c1 - c2;
            double a = d1.Dot(d1), b = d1.Dot(d2), c = d2.Dot(d2), d = d1.Dot(w), e = d2.Dot(w);
            var den = a * c - b * b;
            if (den <= 1e-6) return null;
            var t1 = (b * e - c * d) / den;
            var t2 = (a * e - b * d) / den;
            if (t1 <= 0.2 || t2 <= 0.2) return null;
            return 0.5 * (c1 + t1 * d1 + c2 + t2 * d2);
        }

        /// <summary>Where the camera was relative to the tree for view k, and the depth of a point in that view (how far
        /// it is from the camera along the view direction, relative to the axis: negative is the near side).</summary>
        public double Depth(in Vec3 p, int k) => _sin[k] * p.X + _cos[k] * p.Y + _ty[k];

        private static double[] Mul(double[] a, double[] b, double[]? result = null)
        {
            result ??= new double[9];
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                    result[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
            return result;
        }
    }
}
