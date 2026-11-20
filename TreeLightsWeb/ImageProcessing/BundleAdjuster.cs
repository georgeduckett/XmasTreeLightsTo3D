namespace TreeLightsWeb.ImageProcessing
{
    /// <summary>Where LED <see cref="Point"/> was seen in view <see cref="View"/>, and how much to trust it.</summary>
    public readonly record struct Observation(int Point, int View, double U, double V, double Weight);

    /// <summary>Soft constraints on the camera parameters (they keep the solution sensible when the data alone can't).</summary>
    public sealed record CameraPriors(double[] AngleCentres, double AngleSigma, double ShiftSigma, double FocalLength, double FocalLengthRelativeSigma);

    /// <summary>
    /// Robust bundle adjustment: finds the LED positions (and, unless fixed, the camera parameters) that best explain where
    /// the LEDs were seen, by Levenberg-Marquardt with a Schur complement (each LED only couples to the camera parameters),
    /// using a Cauchy loss (via iteratively reweighted least squares) so the odd bad observation can't drag the solution.
    /// </summary>
    public static class BundleAdjuster
    {
        public sealed record Result(TurntableCamera Camera, Vec3[] Points, double Cost);

        public static double RobustCost(TurntableCamera camera, IReadOnlyList<Vec3> points, ReadOnlySpan<Observation> observations, double cauchy)
        {
            var c2 = cauchy * cauchy;
            var cost = 0.0;
            foreach (var o in observations)
            {
                camera.Project(points[o.Point], o.View, out var u, out var v);
                var e2 = (u - o.U) * (u - o.U) + (v - o.V) * (v - o.V);
                cost += o.Weight * c2 * Math.Log(1 + e2 / c2);
            }
            return cost;
        }

        private static double PriorCost(double[] g, TurntableCamera camera, CameraPriors? priors, double cauchy)
        {
            if (priors == null) return 0;
            var c2 = cauchy * cauchy;
            var wa = c2 / (priors.AngleSigma * priors.AngleSigma);
            var wt = c2 / (priors.ShiftSigma * priors.ShiftSigma);
            var cost = 0.0;
            for (var k = 1; k < camera.Views; k++)
            {
                var da = g[TurntableCamera.IA + k - 1] - priors.AngleCentres[k - 1];
                cost += wa * da * da;
                cost += wt * (g[camera.IX + k - 1] * g[camera.IX + k - 1] + g[camera.IY + k - 1] * g[camera.IY + k - 1]);
            }
            if (priors.FocalLengthRelativeSigma > 0)
            {
                var sf = priors.FocalLengthRelativeSigma * priors.FocalLength;
                cost += c2 / (sf * sf) * (g[0] - priors.FocalLength) * (g[0] - priors.FocalLength);
            }
            return cost;
        }

        public static Result Run(TurntableCamera camera, IReadOnlyList<Vec3> initialPoints, ReadOnlySpan<Observation> observations, double cauchy,
                                 int iterations, CameraPriors? priors, bool fixCamera)
        {
            var n = initialPoints.Count;
            var ng = camera.ParameterCountValue;
            var points = initialPoints.ToArray();
            var g = camera.CopyParameters();
            var c2 = cauchy * cauchy;
            var lambda = 1e-3;
            var cost = RobustCost(camera, points, observations, cauchy) + PriorCost(g, camera, priors, cauchy);

            var vBlocks = new double[n * 9];
            var bp = new double[n * 3];
            var wBlocks = fixCamera ? Array.Empty<double>() : new double[n * ng * 3];
            var u = new double[ng * ng];
            var bg = new double[ng];
            var vInv = new double[n * 9];
            var s = new double[ng * ng];
            var rhs = new double[ng];
            var t = new double[ng * 3];
            Span<double> jp = stackalloc double[6];
            Span<double> jg = stackalloc double[14];
            Span<int> idx = stackalloc int[7];
            Span<double> va = stackalloc double[9];

            for (var it = 0; it < iterations; it++)
            {
                Array.Clear(vBlocks); Array.Clear(bp); Array.Clear(wBlocks); Array.Clear(u); Array.Clear(bg);
                foreach (var o in observations)
                {
                    camera.Project(points[o.Point], o.View, out var pu, out var pv, jp, jg);
                    var ru = pu - o.U;
                    var rv = pv - o.V;
                    var w = o.Weight / (1 + (ru * ru + rv * rv) / c2);
                    var vb = o.Point * 9;
                    for (var a = 0; a < 3; a++)
                    {
                        for (var b = 0; b < 3; b++) vBlocks[vb + a * 3 + b] += w * (jp[a] * jp[b] + jp[3 + a] * jp[3 + b]);
                        bp[o.Point * 3 + a] -= w * (jp[a] * ru + jp[3 + a] * rv);
                    }
                    if (fixCamera) continue;
                    camera.ParameterIndices(o.View, idx);
                    for (var p = 0; p < 7; p++)
                    {
                        var gi = idx[p];
                        if (gi < 0) continue;
                        bg[gi] -= w * (jg[p] * ru + jg[7 + p] * rv);
                        for (var q = 0; q < 7; q++)
                        {
                            var gq = idx[q];
                            if (gq >= 0) u[gi * ng + gq] += w * (jg[p] * jg[q] + jg[7 + p] * jg[7 + q]);
                        }
                        var wb = (o.Point * ng + gi) * 3;
                        for (var a = 0; a < 3; a++) wBlocks[wb + a] += w * (jg[p] * jp[a] + jg[7 + p] * jp[3 + a]);
                    }
                }

                if (!fixCamera && priors != null)
                {
                    var wa = c2 / (priors.AngleSigma * priors.AngleSigma);
                    var wt = c2 / (priors.ShiftSigma * priors.ShiftSigma);
                    for (var k = 1; k < camera.Views; k++)
                    {
                        var ia = TurntableCamera.IA + k - 1;
                        u[ia * ng + ia] += wa; bg[ia] -= wa * (g[ia] - priors.AngleCentres[k - 1]);
                        foreach (var ix in new[] { camera.IX + k - 1, camera.IY + k - 1 })
                        {
                            u[ix * ng + ix] += wt; bg[ix] -= wt * g[ix];
                        }
                    }
                    if (priors.FocalLengthRelativeSigma > 0)
                    {
                        var sf = priors.FocalLengthRelativeSigma * priors.FocalLength;
                        var wf = c2 / (sf * sf);
                        u[0] += wf; bg[0] -= wf * (g[0] - priors.FocalLength);
                    }
                }

                // Damped point blocks and their inverses
                for (var i = 0; i < n; i++)
                {
                    for (var a = 0; a < 9; a++) va[a] = vBlocks[i * 9 + a];
                    for (var a = 0; a < 3; a++) va[a * 4] += lambda * vBlocks[i * 9 + a * 4] + 1e-6;
                    if (!LinearAlgebra.Invert3(va, vInv.AsSpan(i * 9, 9)))
                    {
                        Array.Clear(vInv, i * 9, 9);
                    }
                }

                var dg = new double[ng];
                var newPoints = new Vec3[n];
                if (!fixCamera)
                {
                    // Reduced camera system: S dg = rhs
                    Array.Copy(u, s, u.Length);
                    for (var a = 0; a < ng; a++) s[a * ng + a] += lambda * u[a * ng + a];
                    Array.Copy(bg, rhs, ng);
                    for (var i = 0; i < n; i++)
                    {
                        var wb = i * ng * 3;
                        var vi = i * 9;
                        var any = false;
                        for (var a = 0; a < ng * 3 && !any; a++) any = wBlocks[wb + a] != 0;
                        if (!any) continue;
                        for (var a = 0; a < ng; a++)
                            for (var b = 0; b < 3; b++)
                                t[a * 3 + b] = wBlocks[wb + a * 3] * vInv[vi + b] + wBlocks[wb + a * 3 + 1] * vInv[vi + 3 + b] + wBlocks[wb + a * 3 + 2] * vInv[vi + 6 + b];
                        for (var a = 0; a < ng; a++)
                        {
                            if (t[a * 3] == 0 && t[a * 3 + 1] == 0 && t[a * 3 + 2] == 0) continue;
                            for (var b = 0; b < ng; b++)
                                s[a * ng + b] -= t[a * 3] * wBlocks[wb + b * 3] + t[a * 3 + 1] * wBlocks[wb + b * 3 + 1] + t[a * 3 + 2] * wBlocks[wb + b * 3 + 2];
                            rhs[a] -= t[a * 3] * bp[i * 3] + t[a * 3 + 1] * bp[i * 3 + 1] + t[a * 3 + 2] * bp[i * 3 + 2];
                        }
                    }
                    if (!LinearAlgebra.Solve(s, rhs, ng))
                    {
                        lambda *= 4;
                        if (lambda > 1e8) break;
                        continue;
                    }
                    Array.Copy(rhs, dg, ng);
                }

                for (var i = 0; i < n; i++)
                {
                    double r0 = bp[i * 3], r1 = bp[i * 3 + 1], r2 = bp[i * 3 + 2];
                    if (!fixCamera)
                    {
                        var wb = i * ng * 3;
                        for (var a = 0; a < ng; a++)
                        {
                            if (dg[a] == 0) continue;
                            r0 -= wBlocks[wb + a * 3] * dg[a]; r1 -= wBlocks[wb + a * 3 + 1] * dg[a]; r2 -= wBlocks[wb + a * 3 + 2] * dg[a];
                        }
                    }
                    var vi = i * 9;
                    newPoints[i] = points[i] + new Vec3(vInv[vi] * r0 + vInv[vi + 1] * r1 + vInv[vi + 2] * r2,
                                                        vInv[vi + 3] * r0 + vInv[vi + 4] * r1 + vInv[vi + 5] * r2,
                                                        vInv[vi + 6] * r0 + vInv[vi + 7] * r1 + vInv[vi + 8] * r2);
                }

                var gNew = (double[])g.Clone();
                for (var a = 0; a < ng; a++) gNew[a] += dg[a];
                var cameraNew = fixCamera ? camera : camera.With(gNew);
                var costNew = RobustCost(cameraNew, newPoints, observations, cauchy) + PriorCost(gNew, cameraNew, priors, cauchy);
                if (costNew < cost && !double.IsNaN(costNew))
                {
                    var rel = (cost - costNew) / cost;
                    points = newPoints;
                    g = gNew;
                    camera = cameraNew;
                    cost = costNew;
                    lambda = Math.Max(lambda / 3, 1e-9);
                    if (rel < 1e-8) break;
                }
                else
                {
                    lambda *= 4;
                    if (lambda > 1e8) break;
                }
            }
            return new Result(camera, points, cost);
        }
    }
}
