namespace TreeLightsWeb.ImageProcessing
{
    /// <summary>The 3D positions of the LEDs, the camera they were seen with and how well it all fits.</summary>
    public sealed class ReconstructionResult
    {
        public required TurntableCamera Camera { get; init; }
        /// <summary>The final positions (tree coordinates, see <see cref="TurntableCamera"/>).</summary>
        public required Vec3[] Positions { get; init; }
        /// <summary>Where the images alone (ignoring the wire) would put each LED; null if nowhere.</summary>
        public required Vec3?[] ImageOnlyPositions { get; init; }
        /// <summary>How many views each LED's position is confirmed by (0: estimated from its neighbours).</summary>
        public required int[] ViewsUsed { get; init; }
        /// <summary>The wire picked a different match for the LED than the images alone would have.</summary>
        public required bool[] WireCorrected { get; init; }
        /// <summary>Per LED, per view: the blob used as that LED (null if the view wasn't used).</summary>
        public required LedCandidate?[][] UsedCandidates { get; init; }
        /// <summary>Root mean square distance (pixels) between where each LED was seen and where its position projects.</summary>
        public required double[] RmsError { get; init; }
        /// <summary>LEDs further from both their neighbours than the wire allows there (should be none).</summary>
        public required int FarFromNeighbours { get; init; }
        /// <summary>Leave-one-view-out errors (pixels): each view's observation of an LED predicted from the other views.</summary>
        public required double[] LeaveOneOutErrors { get; init; }
        /// <summary>The height (tree coordinates) of the floor if light on/reflected by it was found and ignored.</summary>
        public double? Floor { get; init; }
        /// <summary>The height of the ceiling if light on it was found and ignored.</summary>
        public double? Ceiling { get; init; }
        /// <summary>LEDs whose best match in the images was light on the floor or ceiling (or things on the floor).</summary>
        public int[] MatchedFloorOrCeiling { get; init; } = [];
        /// <summary>The fit (robust cost) with the tree turned clockwise and anticlockwise.</summary>
        public (double Clockwise, double Anticlockwise) DirectionFits { get; init; }
        public required bool DirectionFromBrightness { get; init; }
        public required double DirectionEvidence { get; init; }
    }

    /// <summary>
    /// Works out the LEDs' 3D positions from where they were seen in the images:
    /// <list type="number">
    /// <item>The actual angles the tree was turned by between views (which can be far from 45 degrees when it's turned by
    /// hand) from a robust factorisation of the LEDs' horizontal positions, and which way it was turned from which side
    /// each LED is brightest on (LEDs on the far side are behind the branches).</item>
    /// <item>The camera (focal length, orientation, turn angles, small shifts of the tree) and the LED positions by
    /// robust bundle adjustment, alternating with picking, for each LED, the blobs that the most views agree on.</item>
    /// <item>For each LED, the distinct positions that several views agree on. Light an LED casts on the floor or
    /// ceiling (and its reflection in a shiny floor) stays under/over it as the tree turns, so views can agree on those
    /// too: they're recognised as a plane of positions all at one height below/above the tree and ignored.</item>
    /// <item>The combination of positions along the string that best balances agreement with the images against
    /// neighbouring LEDs being no further apart than the wire allows (dynamic programming), which rejects other wrong
    /// matches such as reflections.</item>
    /// <item>A final adjustment of camera and positions; LEDs no view could confirm are placed between their
    /// neighbours.</item>
    /// </list>
    /// </summary>
    public sealed class TreeReconstructor
    {
        private const int CalibrationRounds = 3;
        private const int HypothesesPerLed = 5;
        private const double DistinctHypotheses = 0.03;    // tree units (1 = the camera's distance from the tree's axis)
        private const double MissingLedScore = 0.5;        // what having no position is worth, compared with the views agreeing
        private const double WirePenalty = 1.0;
        private const int SpacingWindow = 25;
        private const double SpacingFactor = 3.0;
        private static readonly double AngleSigma = 20 * Math.PI / 180;
        private const double ShiftSigma = 0.02;
        private const double FocalLengthRelativeSigma = 0.35;
        private const double PlaneHalfWidth = 0.0125;      // tree units: how tightly positions on a floor/ceiling cluster in height
        private const double PlaneSurround = 0.05;
        private const double PlaneContrast = 4.0;          // how much denser than the surrounding heights a plane must be
        private const double PlaneMinMargin = 0.015, PlaneMaxMargin = 0.06;
        private const double ClearlyBetterFit = 0.05;      // relative difference in fit that settles which way the tree was turned

        private readonly int _ledCount, _views, _width, _height;
        private readonly double _tolerance, _cauchy;
        private readonly LedCandidate[][] _candidates;   // per LED
        private readonly int[][] _ranks;                 // per LED: each candidate's rank by brightness within its image
        private readonly double[][] _quality;            // per LED, per candidate: how much to trust it (0..1)
        private readonly double _sharpPeak;              // a blob this peaked is (nearly) saturated: an LED seen directly
        private readonly Action<string> _progress;

        public TreeReconstructor(int ledCount, int views, int width, int height, IEnumerable<LedCandidate> candidates, Action<string>? progress = null)
        {
            _ledCount = ledCount;
            _views = views;
            _width = width;
            _height = height;
            var scale = Math.Max(width, height) / 1920.0;
            _tolerance = Math.Max(3, 10 * scale);
            _cauchy = Math.Max(1.5, 5 * scale);
            _progress = progress ?? (_ => { });

            var lists = Enumerable.Range(0, ledCount).Select(_ => new List<LedCandidate>()).ToArray();
            foreach (var c in candidates)
            {
                if (c.Led >= 0 && c.Led < ledCount && c.View >= 0 && c.View < views) lists[c.Led].Add(c);
            }
            _candidates = new LedCandidate[ledCount][];
            _ranks = new int[ledCount][];
            _quality = new double[ledCount][];
            for (var i = 0; i < ledCount; i++)
            {
                var list = lists[i].OrderBy(c => c.View).ThenByDescending(c => c.Flux).ToArray();
                _candidates[i] = list;
                _ranks[i] = new int[list.Length];
                for (var j = 0; j < list.Length; j++) _ranks[i][j] = j > 0 && list[j - 1].View == list[j].View ? _ranks[i][j - 1] + 1 : 0;
                // How much to trust each blob, relative to the LED's best: brighter overall (more of the LED's light), and
                // sharper (a directly visible LED has a saturated core; light it casts on the ceiling, the floor or anything
                // else is spread out and dim, and so are reflections)
                var maxFlux = list.Length > 0 ? list.Max(c => c.Flux) : 1;
                var maxPeak = list.Length > 0 ? list.Max(c => c.Peak) : 1;
                _quality[i] = list.Select(c => Math.Sqrt(Math.Max(c.Flux, 0) / maxFlux * Math.Max(c.Peak, 0) / maxPeak)).ToArray();
            }
            var peaks = _candidates.SelectMany(c => c).Select(c => c.Peak).ToArray();
            _sharpPeak = peaks.Length > 0 ? 0.8 * LinearAlgebra.Percentile(peaks, 99) : double.PositiveInfinity;
        }

        public ReconstructionResult Run()
        {
            _progress("Measuring the angles the tree was turned by");
            var (angles, axisU) = EstimateAngles();
            var evidence = DirectionEvidence(angles, axisU);

            // Which way the tree was turned: the mirror image of the tree turned the other way fits the images nearly as
            // well, apart from the perspective. So fit both, and go by the perspective if one clearly fits better, else by
            // which side of the tree the LEDs are brighter on.
            var clockwise = Calibrate(-1, angles, axisU);
            var anticlockwise = Calibrate(+1, angles, axisU);
            var (better, worse) = clockwise.Cost <= anticlockwise.Cost ? (clockwise, anticlockwise) : (anticlockwise, clockwise);
            Calibration calibration;
            bool fromBrightness;
            if ((worse.Cost - better.Cost) / better.Cost >= ClearlyBetterFit || Math.Abs(evidence) < 0.15)
            {
                calibration = better;
                fromBrightness = false;
            }
            else
            {
                calibration = evidence < 0 ? clockwise : anticlockwise;
                fromBrightness = true;
            }
            var camera = calibration.Camera;

            _progress("Finding the positions the views agree on for each LED");
            var hypotheses = new List<Hypothesis>[_ledCount];
            Parallel.For(0, _ledCount, i => hypotheses[i] = Hypotheses(i, camera, HypothesesPerLed));
            var imageOnly = hypotheses.Select(h => h.Count > 0 ? (Vec3?)h[0].Position : null).ToArray();

            // Light an LED throws on the floor (or ceiling) stays directly under (over) it as the tree is turned, and so
            // does its reflection in a shiny floor, so several views can agree on them just as on the LED itself.
            // They show up as many positions at exactly the same height: if there's such a plane below (above) the
            // tree, ignore anything on or beyond it. And below (above) the lowest (highest) LEDs seen directly, only
            // accept LEDs seen directly: what's there otherwise is light on things on the floor, the stand and so on.
            var limits = FindHeightLimits(hypotheses);
            var onPlanes = Enumerable.Range(0, _ledCount).Where(i => hypotheses[i].Count > 0 && !limits.Allows(hypotheses[i][0])).ToArray();
            if (onPlanes.Length > 0)
            {
                Parallel.For(0, _ledCount, i => hypotheses[i] = Hypotheses(i, camera, HypothesesPerLed, limits));
            }
            var (floor, ceiling) = (limits.Floor, limits.Ceiling);

            var reach = LocalReach(hypotheses.Select(h => h.Count > 0 ? h[0].Position : Vec3.Zero).ToArray(),
                                   hypotheses.Select(h => h.Count > 0 && h[0].Observations.Length >= 3).ToArray());

            _progress("Following the wire from LED to LED");
            var choice = ChooseAlongString(hypotheses, reach);

            // Final adjustment of the camera and the chosen positions
            _progress("Refining the camera and LED positions");
            var observations = new List<Observation>();
            var points = new Vec3[_ledCount];
            for (var i = 0; i < _ledCount; i++)
            {
                if (choice[i] < 0) continue;
                var h = hypotheses[i][choice[i]];
                points[i] = h.Position;
                foreach (var j in h.Observations)
                {
                    var c = _candidates[i][j];
                    observations.Add(new Observation(i, c.View, c.U, c.V, _quality[i][j]));
                }
            }
            var priors = new CameraPriors(Enumerable.Range(1, _views - 1).Select(camera.Angle).ToArray(), AngleSigma, ShiftSigma,
                                          0.75 * Math.Max(_width, _height), FocalLengthRelativeSigma);
            var final = BundleAdjuster.Run(camera, points, observations.ToArray(), _cauchy, 100, priors, fixCamera: false);
            camera = final.Camera;
            points = final.Points;

            // LEDs with no position the views agree on (or only ones the wire rules out) go between their neighbours
            var known = Enumerable.Range(0, _ledCount).Where(i => choice[i] >= 0).ToArray();
            if (known.Length == 0) throw new InvalidOperationException("Couldn't find any LED that two or more views agree on.");
            for (var i = 0; i < _ledCount; i++)
            {
                if (choice[i] >= 0) continue;
                var next = Array.BinarySearch(known, i);
                next = ~next;
                if (next == 0) points[i] = points[known[0]];
                else if (next >= known.Length) points[i] = points[known[^1]];
                else
                {
                    int a = known[next - 1], b = known[next];
                    points[i] = Vec3.Lerp(points[a], points[b], (i - a) / (double)(b - a));
                }
            }

            // Bookkeeping for the report
            var used = new LedCandidate?[_ledCount][];
            var viewsUsed = new int[_ledCount];
            var rms = new double[_ledCount];
            var wireCorrected = new bool[_ledCount];
            var rejected = onPlanes.ToHashSet();
            for (var i = 0; i < _ledCount; i++)
            {
                used[i] = new LedCandidate?[_views];
                wireCorrected[i] = choice[i] != 0 && hypotheses[i].Count > 0 || rejected.Contains(i);
                if (choice[i] < 0) continue;
                var sum = 0.0;
                foreach (var j in hypotheses[i][choice[i]].Observations)
                {
                    var c = _candidates[i][j];
                    used[i][c.View] = c;
                    camera.Project(points[i], c.View, out var u, out var v);
                    sum += (u - c.U) * (u - c.U) + (v - c.V) * (v - c.V);
                    viewsUsed[i]++;
                }
                rms[i] = Math.Sqrt(sum / viewsUsed[i]);
            }

            var far = 0;
            for (var i = 0; i < _ledCount; i++)
            {
                var before = i == 0 || points[i].DistanceTo(points[i - 1]) > reach[i - 1];
                var after = i == _ledCount - 1 || points[i].DistanceTo(points[i + 1]) > reach[i];
                if (before && after && _ledCount > 1) far++;
            }

            return new ReconstructionResult
            {
                Camera = camera,
                Positions = points,
                ImageOnlyPositions = imageOnly,
                ViewsUsed = viewsUsed,
                WireCorrected = wireCorrected,
                UsedCandidates = used,
                RmsError = rms,
                FarFromNeighbours = far,
                LeaveOneOutErrors = LeaveOneViewOut(camera, points, used),
                DirectionFromBrightness = fromBrightness,
                DirectionFits = (clockwise.Cost, anticlockwise.Cost),
                DirectionEvidence = evidence,
                Floor = floor,
                Ceiling = ceiling,
                MatchedFloorOrCeiling = onPlanes,
            };
        }

        // ------------------------------------------------------------------------------------------ turn angles, direction

        /// <summary>
        /// Each LED's horizontal image position in view k is (nearly) axis + x cos(a_k) - y sin(a_k): a rank 2 matrix
        /// (LEDs x views). Factorising it robustly (ignoring LEDs whose brightest blob was something else) gives the views'
        /// directions up to a linear transformation, and requiring them to be unit vectors fixes that, leaving the angles.
        /// </summary>
        private (double[] Angles, double AxisU) EstimateAngles(int iterations = 60)
        {
            int n = _ledCount, nv = _views;
            var u = new double[n * nv];
            var seen = new bool[n * nv];
            for (var i = 0; i < n; i++)
            {
                for (var j = 0; j < _candidates[i].Length; j++)
                {
                    if (_ranks[i][j] != 0) continue;
                    var c = _candidates[i][j];
                    u[i * nv + c.View] = c.U;
                    seen[i * nv + c.View] = true;
                }
            }

            var robust = 3 * _tolerance / 2;
            var b = new double[nv * 2];
            for (var k = 0; k < nv; k++) { b[k * 2] = Math.Cos(k * 2 * Math.PI / nv); b[k * 2 + 1] = -Math.Sin(k * 2 * Math.PI / nv); }
            var a = new double[n * 2];
            var weight = seen.Select(s => s ? 1.0 : 0.0).ToArray();
            var axis = LinearAlgebra.Median(Enumerable.Range(0, n * nv).Where(x => seen[x]).Select(x => u[x]));

            for (var it = 0; it < iterations; it++)
            {
                for (var i = 0; i < n; i++)
                {
                    var observed = 0;
                    for (var k = 0; k < nv; k++) if (weight[i * nv + k] > 0) observed++;
                    if (observed < 3) continue;
                    var design = new double[nv * 2];
                    var rhs = new double[nv];
                    var w = new double[nv];
                    for (var k = 0; k < nv; k++)
                    {
                        design[k * 2] = b[k * 2]; design[k * 2 + 1] = b[k * 2 + 1];
                        rhs[k] = u[i * nv + k] - axis;
                        w[k] = weight[i * nv + k];
                    }
                    var sol = LinearAlgebra.LeastSquares(design, rhs, nv, 2, w);
                    if (sol != null) { a[i * 2] = sol[0]; a[i * 2 + 1] = sol[1]; }
                }
                for (var k = 0; k < nv; k++)
                {
                    var design = new double[n * 3];
                    var rhs = new double[n];
                    var w = new double[n];
                    for (var i = 0; i < n; i++)
                    {
                        design[i * 3] = a[i * 2]; design[i * 3 + 1] = a[i * 2 + 1]; design[i * 3 + 2] = 1;
                        rhs[i] = u[i * nv + k];
                        w[i] = weight[i * nv + k];
                    }
                    var sol = LinearAlgebra.LeastSquares(design, rhs, n, 3, w);
                    if (sol != null) { b[k * 2] = sol[0]; b[k * 2 + 1] = sol[1]; }
                }
                var residuals = new List<double>();
                for (var i = 0; i < n; i++)
                    for (var k = 0; k < nv; k++)
                        if (seen[i * nv + k]) residuals.Add(u[i * nv + k] - a[i * 2] * b[k * 2] - a[i * 2 + 1] * b[k * 2 + 1]);
                axis = LinearAlgebra.Median(residuals);
                for (var i = 0; i < n; i++)
                {
                    for (var k = 0; k < nv; k++)
                    {
                        if (!seen[i * nv + k]) continue;
                        var e = Math.Abs(u[i * nv + k] - axis - a[i * 2] * b[k * 2] - a[i * 2 + 1] * b[k * 2 + 1]);
                        weight[i * nv + k] = 1 / (1 + (e / robust) * (e / robust));
                    }
                }
            }

            // Find Q = G G^T with b_k^T Q b_k = 1 for every view; then the views' directions are b_k G
            var rows = new double[nv * 3];
            for (var k = 0; k < nv; k++)
            {
                rows[k * 3] = b[k * 2] * b[k * 2];
                rows[k * 3 + 1] = 2 * b[k * 2] * b[k * 2 + 1];
                rows[k * 3 + 2] = b[k * 2 + 1] * b[k * 2 + 1];
            }
            var q = LinearAlgebra.LeastSquares(rows, Enumerable.Repeat(1.0, nv).ToArray(), nv, 3)
                    ?? throw new InvalidOperationException("Couldn't work out the turn angles from the LED positions.");
            var (l1, l2, v1, v2) = SymmetricEigen2(q[0], q[1], q[2]);
            double s1 = Math.Sqrt(Math.Abs(l1)), s2 = Math.Sqrt(Math.Abs(l2));
            var angles = new double[nv];
            double? reference = null;
            for (var k = 0; k < nv; k++)
            {
                var c0 = (b[k * 2] * v1.X + b[k * 2 + 1] * v1.Y) * s1;
                var c1 = (b[k * 2] * v2.X + b[k * 2 + 1] * v2.Y) * s2;
                var angle = Math.Atan2(-c1, c0);
                reference ??= angle;
                angles[k] = Mod2Pi(angle - reference.Value);
            }
            var increasing = true;
            for (var k = 0; k < nv; k++) if ((k + 1 < nv ? angles[k + 1] : 2 * Math.PI) < angles[k]) increasing = false;
            if (!increasing || angles[1] > Math.PI)
            {
                for (var k = 0; k < nv; k++) angles[k] = Mod2Pi(-angles[k]);
            }
            return (angles, axis);
        }

        private static (double L1, double L2, (double X, double Y) V1, (double X, double Y) V2) SymmetricEigen2(double a, double b, double c)
        {
            var mean = 0.5 * (a + c);
            var d = Math.Sqrt(0.25 * (a - c) * (a - c) + b * b);
            double l1 = mean - d, l2 = mean + d;
            (double X, double Y) v1;
            if (Math.Abs(b) > 1e-12 * (Math.Abs(a) + Math.Abs(c) + 1e-300))
            {
                var x = b; var y = l1 - a; var len = Math.Sqrt(x * x + y * y);
                v1 = (x / len, y / len);
            }
            else
            {
                v1 = a <= c ? (1, 0) : (0, 1);
            }
            return (l1, l2, v1, (-v1.Y, v1.X));
        }

        private static double Mod2Pi(double a)
        {
            a %= 2 * Math.PI;
            return a < 0 ? a + 2 * Math.PI : a;
        }

        /// <summary>
        /// Which way the tree was turned. Mirror images explain the images almost equally well (only perspective tells them
        /// apart), but LEDs on the far side of the tree are seen through the branches so look dimmer: with the right
        /// direction each LED is brightest in the views where it's nearest the camera. Returns the median (over LEDs) of the
        /// correlation between how far away and how bright (and sharp) the LED is, assuming the tree was turned clockwise (sign -1):
        /// clearly negative means it was; clearly positive means the other way.
        /// </summary>
        private double DirectionEvidence(double[] angles, double axisU)
        {
            var correlations = new List<double>();
            for (var i = 0; i < _ledCount; i++)
            {
                var top = Enumerable.Range(0, _candidates[i].Length).Where(j => _ranks[i][j] == 0 && _candidates[i][j].Flux > 0 && _candidates[i][j].Peak > 0).ToArray();
                if (top.Length < 4) continue;
                var design = new double[top.Length * 2];
                var rhs = new double[top.Length];
                for (var r = 0; r < top.Length; r++)
                {
                    var c = _candidates[i][top[r]];
                    var a = -angles[c.View];
                    design[r * 2] = Math.Cos(a); design[r * 2 + 1] = -Math.Sin(a);
                    rhs[r] = c.U - axisU;
                }
                var xy = LinearAlgebra.LeastSquares(design, rhs, top.Length, 2);
                if (xy == null) continue;
                var depth = top.Select(j => { var a = -angles[_candidates[i][j].View]; return Math.Sin(a) * xy[0] + Math.Cos(a) * xy[1]; }).ToArray();
                var brightness = top.Select(j => Math.Log(_candidates[i][j].Flux * Math.Max(_candidates[i][j].Peak, 1e-3))).ToArray();
                var r_ = Correlation(depth, brightness);
                if (!double.IsNaN(r_)) correlations.Add(r_);
            }
            return correlations.Count > 0 ? LinearAlgebra.Median(correlations) : 0;
        }

        private static double Correlation(double[] x, double[] y)
        {
            double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
            for (var i = 0; i < x.Length; i++)
            {
                sxy += (x[i] - mx) * (y[i] - my);
                sxx += (x[i] - mx) * (x[i] - mx);
                syy += (y[i] - my) * (y[i] - my);
            }
            return sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : double.NaN;
        }

        // ------------------------------------------------------------------------------------------------------ camera

        private sealed record Calibration(TurntableCamera Camera, Vec3[] Points, double Cost);

        private Calibration Calibrate(int sign, double[] angles, double axisU)
        {
            var focal = 0.75 * Math.Max(_width, _height);
            // The tree's axis: the average position of LEDs seen in every view (or of everything)
            var everyView = new List<double>();
            var all = new List<double>();
            var top = new List<(int Led, int Index)>();
            for (var i = 0; i < _ledCount; i++)
            {
                var tops = Enumerable.Range(0, _candidates[i].Length).Where(j => _ranks[i][j] == 0).ToArray();
                top.AddRange(tops.Select(j => (i, j)));
                all.AddRange(tops.Select(j => _candidates[i][j].U));
                if (tops.Length == _views) everyView.Add(tops.Average(j => _candidates[i][j].U));
            }
            var axis = everyView.Count > 0 ? LinearAlgebra.Median(everyView) : LinearAlgebra.Median(all);
            var camera = new TurntableCamera(_views, _width, _height, sign,
                                             TurntableCamera.InitialParameters(_views, focal, Math.Atan((axis - _width / 2.0) / focal), angles));

            // Starting positions (as if the camera were far away)
            var points = new Vec3[_ledCount];
            for (var i = 0; i < _ledCount; i++)
            {
                var tops = Enumerable.Range(0, _candidates[i].Length).Where(j => _ranks[i][j] == 0).ToArray();
                if (tops.Length < 2) continue;
                var design = new double[tops.Length * 2];
                var rhs = new double[tops.Length];
                for (var r = 0; r < tops.Length; r++)
                {
                    var c = _candidates[i][tops[r]];
                    var a = sign * angles[c.View];
                    design[r * 2] = Math.Cos(a); design[r * 2 + 1] = -Math.Sin(a);
                    rhs[r] = (c.U - axis) / focal;
                }
                var xy = LinearAlgebra.LeastSquares(design, rhs, tops.Length, 2);
                if (xy == null) continue;
                points[i] = new Vec3(xy[0], xy[1], -tops.Average(j => _candidates[i][j].V - _height / 2.0) / focal);
            }

            var priors = new CameraPriors(angles.Skip(1).ToArray(), AngleSigma, ShiftSigma, focal, FocalLengthRelativeSigma);
            var observations = top.Select(t => ToObservation(t.Led, t.Index)).ToArray();
            BundleAdjuster.Result result = null!;
            for (var round = 0; round <= CalibrationRounds; round++)
            {
                _progress($"Working out the camera and turn angles ({(sign < 0 ? "clockwise" : "anticlockwise")}): round {round + 1} of {CalibrationRounds + 1}");
                result = BundleAdjuster.Run(camera, points, observations, _cauchy, 200, priors, fixCamera: false);
                camera = result.Camera;
                points = result.Points;
                if (round == CalibrationRounds) break;

                // Re-pick, for each LED, the blobs the most views agree on
                var best = new Hypothesis?[_ledCount];
                var cam = camera;
                Parallel.For(0, _ledCount, i => { var h = Hypotheses(i, cam, 1); best[i] = h.Count > 0 ? h[0] : null; });
                var next = new List<Observation>();
                for (var i = 0; i < _ledCount; i++)
                {
                    if (best[i] is not { } h) continue;
                    points[i] = h.Position;
                    next.AddRange(h.Observations.Select(j => ToObservation(i, j)));
                }
                observations = next.ToArray();
            }
            return new Calibration(camera, points, result.Cost);
        }

        private Observation ToObservation(int led, int index)
        {
            var c = _candidates[led][index];
            return new Observation(led, c.View, c.U, c.V, _quality[led][index]);
        }

        // ------------------------------------------------------------------------------------------------- hypotheses

        private sealed record Hypothesis(double Score, Vec3 Position, int[] Observations, int Sharp);

        /// <summary>Heights outside Min..Max are ruled out; outside SharpMin..SharpMax an LED must be seen directly
        /// (a sharp blob) in at least two views.</summary>
        private sealed record HeightLimits(double? Floor, double? Ceiling, double Min, double Max, double SharpMin, double SharpMax)
        {
            public static readonly HeightLimits None = new(null, null, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity);
            public bool Allows(double z, int sharp) => z >= Min && z <= Max && (sharp >= 2 || (z >= SharpMin && z <= SharpMax));
            public bool Allows(Hypothesis h) => Allows(h.Position.Z, h.Sharp);
        }

        /// <summary>
        /// Distinct positions for an LED that several views agree on, best first. Every pair of blobs from different views
        /// is triangulated; each such position is scored by how many views have a blob within tolerance of where it
        /// projects (weighted by how bright those blobs are compared with the LED's brightest).
        /// </summary>
        private List<Hypothesis> Hypotheses(int led, TurntableCamera camera, int maxHypotheses, HeightLimits? limits = null)
        {
            limits ??= HeightLimits.None;
            var cands = _candidates[led];
            var quality = _quality[led];
            var m = cands.Length;
            var result = new List<Hypothesis>();
            if (m < 2) return result;

            var rays = cands.Select(c => camera.Ray(c.U, c.V, c.View)).ToArray();
            var scored = new List<(double Key, Vec3 X, int[] Chosen)>();
            var chosen = new int[_views];
            var errors = new double[m];
            for (var a = 0; a < m; a++)
            {
                for (var b = a + 1; b < m; b++)
                {
                    if (cands[a].View == cands[b].View) continue;
                    if (TurntableCamera.Triangulate(rays[a].Centre, rays[a].Direction, rays[b].Centre, rays[b].Direction) is not { } x) continue;
                    if (x.Z < limits.Min || x.Z > limits.Max) continue;
                    for (var j = 0; j < m; j++)
                    {
                        camera.Project(x, cands[j].View, out var u, out var v);
                        errors[j] = Math.Sqrt((u - cands[j].U) * (u - cands[j].U) + (v - cands[j].V) * (v - cands[j].V));
                    }
                    var (score, count, errorSum) = Consensus(cands, quality, errors, chosen);
                    if (count >= 2)
                    {
                        scored.Add((score - 1e-4 * errorSum, x, chosen.Where(j => j >= 0).ToArray()));
                    }
                }
            }
            scored.Sort((p, q) => q.Key.CompareTo(p.Key));

            foreach (var (_, x, set) in scored)
            {
                var refined = Refine(x, set, cands, camera);
                var kept = set.Where(j =>
                {
                    camera.Project(refined, cands[j].View, out var u, out var v);
                    return Math.Sqrt((u - cands[j].U) * (u - cands[j].U) + (v - cands[j].V) * (v - cands[j].V)) < _tolerance;
                }).ToArray();
                var sharp = kept.Count(j => cands[j].Peak >= _sharpPeak);
                if (kept.Length < 2 || !limits.Allows(refined.Z, sharp)) continue;
                if (result.Any(h => h.Position.DistanceTo(refined) < DistinctHypotheses)) continue;
                result.Add(new Hypothesis(kept.Sum(j => quality[j]), refined, kept, sharp));
                if (result.Count >= maxHypotheses) break;
            }
            result.Sort((p, q) => q.Score.CompareTo(p.Score));
            return result;
        }

        /// <summary>For each view, the best blob (brightest, then closest) within tolerance. Fills chosen (by view, -1 for
        /// none) and returns the summed quality, the number of views and the summed error.</summary>
        private (double Score, int Count, double ErrorSum) Consensus(LedCandidate[] cands, double[] quality, double[] errors, int[] chosen)
        {
            Array.Fill(chosen, -1);
            var best = new double[_views];
            for (var j = 0; j < cands.Length; j++)
            {
                if (errors[j] >= _tolerance) continue;
                var k = cands[j].View;
                var key = quality[j] - 1e-3 * errors[j];
                if (chosen[k] < 0 || key > best[k]) { chosen[k] = j; best[k] = key; }
            }
            double score = 0, errorSum = 0;
            var count = 0;
            for (var k = 0; k < _views; k++)
            {
                if (chosen[k] < 0) continue;
                score += quality[chosen[k]];
                errorSum += errors[chosen[k]];
                count++;
            }
            return (score, count, errorSum);
        }

        /// <summary>Gauss-Newton refinement of a point from its (consistent) observations.</summary>
        private static Vec3 Refine(Vec3 x, int[] observations, LedCandidate[] cands, TurntableCamera camera, int iterations = 10)
        {
            Span<double> jp = stackalloc double[6];
            Span<double> jg = stackalloc double[14];
            var ata = new double[9];
            var atb = new double[3];
            for (var it = 0; it < iterations; it++)
            {
                Array.Clear(ata); Array.Clear(atb);
                foreach (var j in observations)
                {
                    camera.Project(x, cands[j].View, out var u, out var v, jp, jg);
                    double ru = u - cands[j].U, rv = v - cands[j].V;
                    for (var a = 0; a < 3; a++)
                    {
                        for (var b = 0; b < 3; b++) ata[a * 3 + b] += jp[a] * jp[b] + jp[3 + a] * jp[3 + b];
                        atb[a] -= jp[a] * ru + jp[3 + a] * rv;
                    }
                }
                for (var a = 0; a < 3; a++) ata[a * 4] += 1e-6;
                if (!LinearAlgebra.Solve(ata, atb, 3)) break;
                var dx = new Vec3(atb[0], atb[1], atb[2]);
                x += dx;
                if (dx.Length < 1e-7) break;
            }
            return x;
        }

        /// <summary>
        /// Looks for a floor (below the tree) and a ceiling (above it): a height at which the positions the views agree on
        /// are much denser than at the heights just around it, and which is mostly only the second (or later) choice for the
        /// LEDs concerned (an LED's light on the floor usually comes with the LED itself being seen too, whereas LEDs that
        /// really are at the same height, like a horizontal ring of them, are each one's first choice).
        /// </summary>
        private static (double? Floor, double? FloorCut, double? Ceiling, double? CeilingCut) FindPlanes(List<Hypothesis>[] hypotheses)
        {
            (double Z, double Score, bool First)[] all = hypotheses.SelectMany(hs => hs.Select((h, rank) => (h.Position.Z, h.Score, rank == 0)))
                                                                  .OrderBy(h => h.Item1).ToArray();
            var firsts = hypotheses.Where(hs => hs.Count > 0).Select(hs => hs[0].Position.Z).ToArray();
            if (all.Length < 50 || firsts.Length < 20) return (null, null, null, null);
            var total = all.Sum(h => h.Score);
            var zs = all.Select(h => h.Z).ToArray();
            double lowLimit = LinearAlgebra.Percentile(firsts.ToArray(), 20), highLimit = LinearAlgebra.Percentile(firsts.ToArray(), 80);

            // Total score of the positions within +-half of a height
            double Density(double z, double half)
            {
                var sum = 0.0;
                var j = Array.BinarySearch(zs, z - half); if (j < 0) j = ~j;
                for (; j < all.Length && all[j].Z <= z + half; j++) sum += all[j].Score;
                return sum;
            }

            (double? Plane, double? Cut) Find(bool below)
            {
                double best = 0, bestZ = double.NaN;
                double from = below ? Math.Max(zs[0], lowLimit - 1) : highLimit, to = below ? lowLimit : Math.Min(zs[^1], highLimit + 1);
                for (var z = from; z <= to; z += 0.0025)
                {
                    var narrow = Density(z, PlaneHalfWidth);
                    var wide = Density(z, PlaneSurround) - narrow;
                    var contrast = narrow / (2 * PlaneHalfWidth) / Math.Max(wide / (2 * (PlaneSurround - PlaneHalfWidth)), 1e-9);
                    if (narrow >= 0.02 * total && contrast > best) { best = contrast; bestZ = z; }
                }
                if (best < PlaneContrast) return (null, null);
                var band = all.Where(h => Math.Abs(h.Z - bestZ) <= PlaneHalfWidth).ToArray();
                if (band.Count(h => h.First) > 0.4 * band.Length) return (null, null);
                var plane = band.Sum(h => h.Z * h.Score) / band.Sum(h => h.Score);
                // Cut where the light on the plane tails off: the quietest height just beyond it (towards the tree)
                double cut = plane, quietest = double.PositiveInfinity;
                for (var offset = PlaneMinMargin; offset <= PlaneMaxMargin + 1e-9; offset += 0.0025)
                {
                    var z = below ? plane + offset : plane - offset;
                    var density = Density(z, PlaneHalfWidth);
                    if (density < quietest) { quietest = density; cut = z; }
                }
                return (plane, cut);
            }
            var (floor, floorCut) = Find(below: true);
            var (ceiling, ceilingCut) = Find(below: false);
            return (floor, floorCut, ceiling, ceilingCut);
        }

        private HeightLimits FindHeightLimits(List<Hypothesis>[] hypotheses)
        {
            var (floor, floorCut, ceiling, ceilingCut) = FindPlanes(hypotheses);
            double min = floorCut ?? double.NegativeInfinity, max = ceilingCut ?? double.PositiveInfinity;
            var direct = hypotheses.SelectMany(hs => hs).Where(h => h.Sharp >= 2 && h.Position.Z >= min && h.Position.Z <= max)
                                   .Select(h => h.Position.Z).ToArray();
            double sharpMin = double.NegativeInfinity, sharpMax = double.PositiveInfinity;
            if (direct.Length >= 50)
            {
                sharpMin = LinearAlgebra.Percentile(direct.ToArray(), 0.5) - 0.01;
                sharpMax = LinearAlgebra.Percentile(direct.ToArray(), 99.5) + 0.01;
            }
            return new HeightLimits(floor, ceiling, min, max, sharpMin, sharpMax);
        }

        // ------------------------------------------------------------------------------------------------------- wire

        /// <summary>
        /// How far apart neighbouring LEDs can plausibly be, around each point on the string: a multiple of the median
        /// distance between neighbouring well-confirmed LEDs nearby (where the wire is bunched up the LEDs are closer than
        /// where it's pulled straight, and a string can be made of strings with different spacings).
        /// </summary>
        private static double[] LocalReach(Vec3[] points, bool[] reliable)
        {
            var n = points.Length;
            if (n < 2) return [];
            var d = new double[n - 1];
            var good = new bool[n - 1];
            for (var i = 0; i < n - 1; i++)
            {
                d[i] = points[i].DistanceTo(points[i + 1]);
                good[i] = reliable[i] && reliable[i + 1];
            }
            var all = Enumerable.Range(0, n - 1).Where(i => good[i]).Select(i => d[i]).ToArray();
            var global = all.Length > 0 ? SpacingFactor * LinearAlgebra.Median(all) : 0.1;
            var reach = new double[n - 1];
            for (var i = 0; i < n - 1; i++)
            {
                var window = new List<double>();
                for (var j = Math.Max(0, i - SpacingWindow); j < Math.Min(n - 1, i + SpacingWindow + 1); j++)
                    if (good[j]) window.Add(d[j]);
                reach[i] = window.Count >= 5 ? SpacingFactor * LinearAlgebra.Median(window) : global;
            }
            var floor = 0.5 * LinearAlgebra.Median(reach.ToArray());
            for (var i = 0; i < n - 1; i++) reach[i] = Math.Max(reach[i], floor);
            return reach;
        }

        /// <summary>
        /// Picks one of each LED's hypotheses (or none) so as to maximise the total agreement of the views minus a penalty
        /// for neighbouring LEDs being further apart than the wire allows ((d / reach - 1)^2 beyond it). Viterbi dynamic
        /// programming along the string. Returns the index of the chosen hypothesis for each LED, or -1 for none.
        /// </summary>
        private static int[] ChooseAlongString(List<Hypothesis>[] hypotheses, double[] reach)
        {
            var n = hypotheses.Length;
            var score = new double[n][];
            var back = new int[n][];
            score[0] = hypotheses[0].Select(h => h.Score).Append(MissingLedScore).ToArray();
            for (var i = 1; i < n; i++)
            {
                var prev = hypotheses[i - 1];
                var cur = hypotheses[i];
                score[i] = new double[cur.Count + 1];
                back[i] = new int[cur.Count + 1];
                for (var b = 0; b <= cur.Count; b++)
                {
                    var best = double.NegativeInfinity;
                    var arg = 0;
                    for (var a = 0; a <= prev.Count; a++)
                    {
                        var penalty = 0.0;
                        if (a < prev.Count && b < cur.Count)
                        {
                            var r = prev[a].Position.DistanceTo(cur[b].Position) / reach[i - 1];
                            if (r > 1) penalty = WirePenalty * (r - 1) * (r - 1);
                        }
                        var v = score[i - 1][a] - penalty;
                        if (v > best) { best = v; arg = a; }
                    }
                    score[i][b] = best + (b < cur.Count ? cur[b].Score : MissingLedScore);
                    back[i][b] = arg;
                }
            }
            var path = new int[n];
            var last = score[n - 1];
            path[n - 1] = Array.IndexOf(last, last.Max());
            for (var i = n - 1; i > 0; i--) path[i - 1] = back[i][path[i]];
            for (var i = 0; i < n; i++) if (path[i] >= hypotheses[i].Count) path[i] = -1;
            return path;
        }

        // ------------------------------------------------------------------------------------------------- accuracy

        /// <summary>For every LED seen in 3 or more views, rebuilds it without each view in turn and measures how far from
        /// that view's observation it then projects: an honest estimate of the positions' accuracy (in pixels).</summary>
        private double[] LeaveOneViewOut(TurntableCamera camera, Vec3[] points, LedCandidate?[][] used)
        {
            var errors = new List<double>[_ledCount];
            Parallel.For(0, _ledCount, i =>
            {
                errors[i] = [];
                var obs = used[i].Where(c => c != null).Select(c => c!.Value).ToArray();
                if (obs.Length < 3) return;
                for (var h = 0; h < obs.Length; h++)
                {
                    var rest = obs.Where((_, j) => j != h).ToArray();
                    var x = Refine(points[i], Enumerable.Range(0, rest.Length).ToArray(), rest, camera);
                    camera.Project(x, obs[h].View, out var u, out var v);
                    errors[i].Add(Math.Sqrt((u - obs[h].U) * (u - obs[h].U) + (v - obs[h].V) * (v - obs[h].V)));
                }
            });
            return errors.SelectMany(e => e).ToArray();
        }
    }
}
