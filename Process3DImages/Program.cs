using HSG.Numerics;
using OpenCvSharp;
using System.Linq;

var LEDCount = 400;

var Points = Enumerable.Range(0, LEDCount).Select(i => new Point(i)).ToArray();

var blurAmount = (double)5;
const double BrightnessThreshold = 200;

MinMaxLocResult ImageBP(string filePath)
{
    // Look for a specific, even colour (or specifically not red in my case)
    using var image = Cv2.ImRead(filePath);
    using var gray = new Mat(); // The greyscale image
    using var hsv = new Mat(); // The HSV version
    using var notRedMask = new Mat(); // The mask to block out colours
    using var masked = new Mat(); // THe masked version of the gray image
    image.CopyTo(gray);
    Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
    Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
    
    Cv2.InRange(hsv, new Scalar(155, 25, 0), new Scalar(180, 255, 255), notRedMask); // Find red
    Cv2.BitwiseNot(notRedMask, notRedMask); // find not-red
    gray.CopyTo(masked, notRedMask);

    Cv2.GaussianBlur(masked, masked, new Size(blurAmount, blurAmount), 0);
    Cv2.MinMaxLoc(masked, out var minVal, out var maxVal, out var minLoc, out var maxLoc);



    if (maxVal < BrightnessThreshold)
    {
        maxVal = 0;
    }
    else
    {
        // Only draw the circle if we're counting it
        Cv2.Circle(image, maxLoc, 41, new Scalar(0, 0, 255), 2);
    }
    Cv2.ImWrite($"{filePath[..filePath.LastIndexOf('.')]}_foundLoc.png", image);
    Cv2.ImWrite($"{filePath[..filePath.LastIndexOf('.')]}_masked.png", masked);

    return new MinMaxLocResult(minVal, maxVal, minLoc, maxLoc, new OpenCvSharp.Point(image.Width, image.Height));
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

    // TODO: Check the Y values, and discount any that aren't close to the average.
    // Work out the (unweighted) average
    var averageY = point.iy.Sum() / point.iy.Where(y => y != 0).Count();

    const int minPointsToKeep = 4;

    var iyOrderOfDeltaFromAvg = Enumerable.Range(0, point.iy.Length)
                                          .OrderByDescending(i => Math.Abs(point.iy[i] - averageY))
                                          .Select(i => i);

    foreach(var iyIndex in iyOrderOfDeltaFromAvg)
    {
        if (point.iw[iyIndex] == 0) continue; // We've already discounted this one, so just carry on
        // Always have at least 4 points
        if (point.iw.Count(w => w != 0) <= 4) break;
        if (Math.Abs(point.iy[iyIndex] - averageY) <= 40) break; // If we're within 20 of the average that's fine
        point.iw[iyIndex] = 0;
        // TODO: Maybe have the routine circle the found pixel now, so we can choose not to here

        // TODO: Maybe another way of doing this is when equation solving, try removing some points to see if we get a much closer solution to the equations

    }

    // Make the weights add up to one
    var weightsSum = point.iw.Sum();

    for (var i = 0; i < point.iw.Length; i++)
    {
        point.iw[i] /= weightsSum;
    }
}

// Find the average Ys
foreach(var point in Points)
{
    Console.Write($"\nSet average Ys. {point.index} of {Points.Length}");
    point.actualy = point.WeightedAverageYs();
}

// Find the average Xs (so we can have zero as the origin of X and Y coords)
var average_xs = Points.Select(p => p.WeightedAverageXs()).ToArray();

var average_x = average_xs.Sum() / average_xs.Length;

// We hard-code the X value to be the centre of the image.
// In theory we don't need to do that as the average of all X values of an LED should be the its.
using (var firstpointImage = Cv2.ImRead(Points.First().imagepath[0]))
{
    average_x = firstpointImage.Width / 2;
}

// TODO: What we should really do is take the average of all X measurements in a given angle then adjust those by that average, for each angle.

// Adjust all x values so the origin is the average of all Xs
foreach(var point in Points)
{
    for(int i = 0; i < point.ix.Length; i++)
    {
        point.ix[i] -= average_x;
    }
}

// Adjust all the Y values so the origin is the max Y (bottom of image as origin in OpenCV is top left) and +ve y's go up
var maxY = Points.Max(p => p.actualy);
foreach (var point in Points)
{
    point.actualy = maxY - point.actualy;
}

IEnumerable<(T First, T Second)> CombinationsOfTwo<T>(T[] list)
{
    for (var i = 0; i < list.Length-1; i++)
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
    var permutations = CombinationsOfTwo(point.eqn.Zip(point.iw)
                                                  .Where(pair => pair.Second != 0) // Ignore equations with a weighting of zero
                                                  .Select(pair => new { Equation = pair.First, Weight = pair.Second })
                                                  .ToArray()).ToArray();

    var rValues = new List<double>(permutations.Length);
    var thetaValues = new List<double>(permutations.Length);
    var weightValues = new List<double>(permutations.Length);

    for (int i = 0; i < permutations.Length; i++)
    {
        // Use HSG.Numerics to solve the system of (2) non-linear equations

        // This function takes in the variables (in r, theta order) and runs the two equations and returns an array with the two results
        double[] FuncsToSolve(double[] variables)
        {
            var result = new double[2]; // There are two equations

            result[0] = permutations[i].First.Equation(variables[0], variables[1]);
            result[1] = permutations[i].Second.Equation(variables[0], variables[1]);
            return result;
        }

        (var solutions, var bestEquationResults, var info) = Fsolve.Fsolver(FuncsToSolve, 2, [400.0, 0.0], 1e-10);

        Console.WriteLine($"Equation results: (r={solutions[0]},theta={solutions[1]}) gives results {bestEquationResults[0]} and {bestEquationResults[1]}");
        point.equationsolverdelta += bestEquationResults.Select(Math.Abs).Sum();

        rValues.Add(solutions[0]);
        thetaValues.Add(solutions[1]);
        weightValues.Add(permutations[i].First.Weight * permutations[i].Second.Weight);
    }

    var values = Enumerable.Zip(rValues, thetaValues, weightValues);

    var rSum = values.Sum(values => values.First * values.Third * values.Third);
    var thetaSum = values.Sum(values => values.Second * values.Third * values.Third);
    var wSum = values.Sum(values => values.Third * values.Third);

    point.r = rSum / (wSum + 0.000000001);
    point.theta = thetaSum / (wSum + 0.000000001);
}



// Now write the csv
File.WriteAllLines("coordinates.csv", new[] { "index, x, y, z, r, theta, equdelta" }
    .Concat(Points.Select(p => $"{p.index}, {p.r * Math.Cos(p.theta!.Value)}, {p.r * Math.Sin(p.theta!.Value)}, {p.actualy}, {p.r}, {p.theta}, {p.equationsolverdelta}")));


Console.WriteLine();
Console.WriteLine($"Done, with an equation solver delta sum of {Points.Sum(p => p.equationsolverdelta)} and average of {Points.Average(p => p.equationsolverdelta)}");






public record MinMaxLocResult(double MinVal, double MaxVal, OpenCvSharp.Point MinLoc, OpenCvSharp.Point MaxLoc, OpenCvSharp.Point imageSize);
public class Point
{
    private const int TreeRotations = 8;
    private const string ImageBasePath = "..\\..\\..\\..\\XmasTreeLightsTo3DCaptureImages\\bin\\Debug\\net8.0";
    public int index;
    public double? r;
    public double? theta;
    public string[] imagepath = new string[8];

    public double? actualy;

    // the positions to the x values
    public double[] ix = new double[TreeRotations];
    // the positon to the y values
    public double[] iy = new double[TreeRotations];
    // the the weights
    public double[] iw = new double[TreeRotations];

    public double equationsolverdelta;
    public Func<double, double, double>[] eqn = new Func<double, double, double>[TreeRotations];

    public Point(int i)
    {
        index = i;
        imagepath = Enumerable.Range(0, eqn.Length).Select(j => Path.Join(ImageBasePath, $"{index}_{j * 45}.png")).ToArray();

        var fullRotation = Math.PI * 2;
        var rotateAngle = fullRotation / TreeRotations;

        // TODO: These are written out explicitly as i'm not sure about closures if I do it in a loop; once working could try it
        eqn[0] = (r, theta) => r * Math.Cos(theta + 0 * rotateAngle) - ix[0];
        eqn[1] = (r, theta) => r * Math.Cos(theta + 1 * rotateAngle) - ix[1];
        eqn[2] = (r, theta) => r * Math.Cos(theta + 2 * rotateAngle) - ix[2];
        eqn[3] = (r, theta) => r * Math.Cos(theta + 3 * rotateAngle) - ix[3];
        eqn[4] = (r, theta) => r * Math.Cos(theta + 4 * rotateAngle) - ix[4];
        eqn[5] = (r, theta) => r * Math.Cos(theta + 5 * rotateAngle) - ix[5];
        eqn[6] = (r, theta) => r * Math.Cos(theta + 6 * rotateAngle) - ix[6];
        eqn[7] = (r, theta) => r * Math.Cos(theta + 7 * rotateAngle) - ix[7];
    }


    public override string ToString()
    {
        return $"the x values are: {string.Join(", ", ix)}, y values are: {string.Join(", ", iy)}, the weights are: {string.Join(", ", iw)}";
    }

    public double WeightedAverageYs()
    {
        return iy.Zip(iw).Sum(pair => pair.First * pair.Second);
    }

    public double WeightedAverageXs()
    {
        return ix.Zip(iw).Sum(pair => pair.First * pair.Second);
    }
}