using HSG.Numerics;
using OpenCvSharp;

var LEDCount = 400;

var Points = Enumerable.Range(0, LEDCount).Select(i => new Point(i)).ToArray();

var blurAmount = (double)41;

MinMaxLocResult ImageBP(string filePath)
{
    using var image = Cv2.ImRead(filePath);
    using var orig = new Mat();
    image.CopyTo(orig);
    using var gray = new Mat();
    Cv2.CvtColor(orig, gray, ColorConversionCodes.BGR2GRAY);
    Cv2.MinMaxLoc(gray, out var minVal, out var maxVal, out var minLoc, out var maxLoc);
    return new MinMaxLocResult(minVal, maxVal, minLoc, maxLoc);
}


var ImageBasePath = "C:\\Users\\Lucy Duckett\\Source\\repos\\XmasTree\\XmasTree\\01_calibration";

// Set all paths
foreach (var point in Points)
{
    for (var i = 0; i < point.imagepath.Length; i++)
    {
        point.imagepath[i] = Path.Join(ImageBasePath, $"{point.index}_{i * 45}.png");
    }
}

// Find brightest points
foreach(var point in Points)
{
    Console.Write($"\rFind brightest points. {point.index} of {Points.Length}");
    for (var i = 0; i < point.imagepath.Length; i++)
    {
        var minMax = ImageBP(point.imagepath[i]);
        point.ix[i] = minMax.MaxLoc.X;
        point.iy[i] = minMax.MaxLoc.Y;
        point.iw[i] = minMax.MaxVal;
    }
}

// Find the average Ys
foreach(var point in Points)
{
    Console.Write($"\nSet average Ys. {point.index} of {Points.Length}");
    point.actualy = point.averageYs();
}

// Find the average Xs (so we can have zero as the origin of X and Z coords)
var average_xs = Points.Select(p => p.averageXs()).ToArray();

var average_x = average_xs.Sum() / (average_xs.Length * 8); // We multiply by 8 here since we don't earlier, and I'm replicating the code

// They manually change the Xs (to 300), but I'm not doing that

// Adjust all x values
foreach(var point in Points)
{
    for(int i = 0; i < point.ix.Length; i++)
    {
        point.ix[i] -= average_x;
    }
}

IEnumerable<(T First, T Second)> CombinationsOfTwo<T>(T[] list)
{
    for (int i = 0; i < list.Length-1; i++)
    {
        for (var j = i + 1; j < list.Length; j++)
        {
            yield return (list[i], list[j]);
        }
    }
}

// Solve the equations to go from x position on image and tree rotation, an r and theta (radius and angle?)
foreach (var point in Points)
{
    Console.Write($"\rSolve equations. {point.index} of {Points.Length}");
    var equ_Permutations = CombinationsOfTwo(point.eqn).ToArray();
    var weights_Permutations = CombinationsOfTwo(point.iw).ToArray();

    var rValues = new List<double>(equ_Permutations.Length);
    var thetaValues = new List<double>(equ_Permutations.Length);
    var weightValues = new List<double>(equ_Permutations.Length);

    for (int i = 0; i < equ_Permutations.Length; i++)
    {
        // Use HSG.Numerics to solve the system of (2) non-linear equations

        // This functions takes in the variables (in r, theta order) and runs the two equations and returns an array with the two results
        double[] FuncsToSolve(double[] variables)
        {
            var result = new double[2]; // There are two equations

            result[0] = equ_Permutations[i].First(variables[0], variables[1]);
            result[1] = equ_Permutations[i].Second(variables[0], variables[1]);
            return result;
        }

        (var solutions, var bestEquationResults, var info) = Fsolve.Fsolver(FuncsToSolve, 2, [0.0, 0.0], 1e-10);

        rValues.Add(solutions[0]);
        thetaValues.Add(solutions[1]);
        weightValues.Add(weights_Permutations[i].First * weights_Permutations[i].Second);
    }

    var values = Enumerable.Zip(rValues, thetaValues, weightValues);

    var rSum = values.Sum(values => values.First * values.Third * values.Third);
    var thetaSum = values.Sum(values => values.Second * values.Third * values.Third);
    var wSum = values.Sum(values => values.Third * values.Third);

    point.r = rSum / (wSum + 0.000000001);
    point.theta = thetaSum / (wSum + 0.000000001);
}



// Now write the csv
File.WriteAllLines("coordinates.csv", new[] { "index, x, y, z, r, theta" }
    .Concat(Points.Select(p => $"{p.index}, {p.r * Math.Cos(p.theta!.Value)}, {p.r * Math.Sin(p.theta!.Value)}, {p.actualy}, {p.r}, {p.theta}")));
foreach(var point in Points)
{
    var index = point.index;
    var x = point.r * Math.Cos(point.theta!.Value);
    var y = point.r * Math.Sin(point.theta!.Value);
    var z = point.actualy;
    var r = point.r;
    var theta = point.theta!.Value;
}

public record MinMaxLocResult(double MinVal, double MaxVal, OpenCvSharp.Point MinLoc, OpenCvSharp.Point MaxLoc);
public class Point
{
    public int index;
    public double? r;
    public double? theta;
    public string[] imagepath = new string[8];

    public double? actualy;

    // the positions to the x values
    public double[] ix = new double[8];
    // the positon to the y values
    public double[] iy = new double[8];
    // the the weights
    public double[] iw = new double[8];
    public Func<double, double, double>[] eqn = new Func<double, double, double>[8];

    public Point(int i)
    {
        index = i;

        eqn[0] = (r, theta) => r * Math.Cos(theta + 0 * Math.PI / 4) - ix[0];
        eqn[1] = (r, theta) => r * Math.Cos(theta + 1 * Math.PI / 4) - ix[1];
        eqn[2] = (r, theta) => r * Math.Cos(theta + 2 * Math.PI / 4) - ix[2];
        eqn[3] = (r, theta) => r * Math.Cos(theta + 3 * Math.PI / 4) - ix[3];
        eqn[4] = (r, theta) => r * Math.Cos(theta + 4 * Math.PI / 4) - ix[4];
        eqn[5] = (r, theta) => r * Math.Cos(theta + 5 * Math.PI / 4) - ix[5];
        eqn[6] = (r, theta) => r * Math.Cos(theta + 6 * Math.PI / 4) - ix[6];
        eqn[7] = (r, theta) => r * Math.Cos(theta + 7 * Math.PI / 4) - ix[7];
    }


    public override string ToString()
    {
        return $"the x values are: {string.Join(", ", ix)}, y values are: {string.Join(", ", iy)}, the weights are: {string.Join(", ", iw)}";
    }

    public double averageYs()
    {
        var y_s = new[] { iy[0] * iw[0], iy[1] * iw[1], iy[2] * iw[2], iy[3] * iw[3], iy[4] * iw[4], iy[5] * iw[5], iy[6] * iw[6], iy[7] * iw[7] };
        return y_s.Sum() / iw.Sum(); // TODO: Shouldn't we also divide by the number of them?? (8) We don't as we do that when getting average
    }

    public double averageXs()
    {
        var x_s = new[] { ix[0] * iw[0], ix[1] * iw[1], ix[2] * iw[2], ix[3] * iw[3], ix[4] * iw[4], ix[5] * iw[5], ix[6] * iw[6], ix[7] * iw[7] };
        return x_s.Sum() / iw.Sum(); // TODO: Shouldn't we also divide by the number of them?? (8) We don't as we do that when getting average
    }
}