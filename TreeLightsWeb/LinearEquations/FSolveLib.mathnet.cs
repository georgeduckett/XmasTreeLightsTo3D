using MathNet.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;

namespace TreeLightsWeb
{
    public static partial class FSolveLibMathNet
    {
        public delegate double[] UserFunction(double[] x);
        /// <summary>
        /// Solve r(x) = 0 by minimizing sum_i r_i(x)^2 using MathNet's Levenberg-Marquardt.
        /// Returns (solutionParameters, minimizedResiduals, statusMessage).
        /// </summary>
        public static Tuple<double[], double[], string> Fsolver(
            UserFunction func,
            int unknownVariableCount,
            double[] xGuess,
            double tolerance)
        {
            ArgumentNullException.ThrowIfNull(func);
            ArgumentNullException.ThrowIfNull(xGuess);
            if (xGuess.Length != unknownVariableCount)
                throw new ArgumentException("xGuess length must equal unknownVariableCount.");

            // Convert initial guess to MathNet vector
            var initialGuess = Vector<double>.Build.DenseOfArray(xGuess);

            // Evaluate user function on initial guess to determine number of residuals (m)
            double[] initialResiduals = func(xGuess);
            if (initialResiduals == null)
                throw new Exception("User function returned null residuals.");

            int residualCount = initialResiduals.Length;
            if (residualCount == 0)
                throw new Exception("User function must return at least one residual.");

            // Build observedX as a dummy vector of indices (0..m-1). The model function below ignores it,
            // but ObjectiveFunction.NonlinearModel expects an observedX vector with the same length as observedY.
            var observedX = Vector<double>.Build.Dense(residualCount, i => i);

            // Observed y-values are zeros -> we are solving r(x) = 0
            var observedY = Vector<double>.Build.Dense(residualCount, 0.0);

            // Weight vector (all ones by default)
            var weight = Vector<double>.Build.Dense(residualCount, 1.0);

            // Model function: given parameter vector p and an observedX entry vector (we ignore observedX),
            // return the model-predicted y-values -- here: the residual vector r(p).
            Func<Vector<double>, Vector<double>, Vector<double>> modelFunc =
                (parameters, xObs) =>
                {
                    // Convert parameter Vector -> double[]
                    var pArr = parameters.ToArray();
                    double[] r = func(pArr);
                    // Defensive check
                    if (r == null || r.Length != residualCount)
                        throw new InvalidOperationException("UserFunction must always return residuals of constant length = " + residualCount);
                    return Vector<double>.Build.DenseOfArray(r);
                };

            // Create IObjectiveModel using NonlinearModel (objective: modelFunc with observedX/observedY/weight).
            // MathNet will compute a numerical Jacobian if not supplied.
            var objectiveModel = ObjectiveFunction.NonlinearModel(modelFunc, observedX, observedY, weight);

            var lm = new LevenbergMarquardtMinimizer();

            // Run minimization
            var result = lm.Minimum(objectiveModel, initialGuess, null, null, null, null, 0.001, tolerance, tolerance, tolerance);
            
            // result.MinimizingPoint -> best-fit parameters
            // result.MinimizedValues -> y-values of the fitted model at the minimized point -- in our formulation
            //                         -> these are the residuals r(x*) (since observedY was zeros)
            var solution = result.MinimizingPoint.ToArray();
            var minimizedResiduals = result.MinimizedValues.ToArray();

            // Compose a status message
            string status = $"ExitReason: {result.ReasonForExit}; Iterations: {result.Iterations}";

            // Optionally: calculate L2 norm of minimized residuals for extra info
            double l2 = Math.Sqrt(minimizedResiduals.Select(v => v * v).Sum());
            status += $"; Residual L2 norm: {l2:E6}";

            return Tuple.Create(solution, minimizedResiduals, status);
        }
    }
}