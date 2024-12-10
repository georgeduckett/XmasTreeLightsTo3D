using HSG.Numerics;
using OpenCvSharp;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;

var LEDCount = 400; // TODO: Get this from WLED

var blurAmount = (double)5;
const double BrightnessThreshold = 20; // A point must be at least this bright to be considered the brightest point
const int MinPointsToKeep = 40; // When considering points across tree rotations, keep at least this many no matter how differnt the Y image values
const int MaxYDiffBetweenTreeRotations = 20; // When considering an LED's image Y values accross tree rotations, disgard any different too from the average
const double proportionToAssumeCorrectDistances = 0.80; // Proportion of distances between LEDs to consider to be correct

var Points = Enumerable.Range(0, LEDCount).Select(i => new Point(i)).ToArray();

MinMaxLocResult ImageBP(string filePath)
{
    // Look for a specific, even colour (or specifically not red in my case)
    using var image = Cv2.ImRead(filePath);
    using var gray = new Mat(); // The greyscale image
    using var masked = new Mat(); // THe masked version of the gray image
    image.CopyTo(gray);
    Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

    // Blank out the power led in the images where it's visible
    if (filePath.EndsWith("_0.png"))
    {
        Cv2.Circle(gray, 296, 476, 5, new Scalar(0), -1);
    }
    else if (filePath.EndsWith("_270.png"))
    {
        Cv2.Circle(gray, 222, 479, 5, new Scalar(0), -1);
    }
    else if (filePath.EndsWith("_315.png"))
    {
        Cv2.Circle(gray, 238, 478, 9, new Scalar(0), -1);
    }

    // TODO: Use ideas in here maybe: https://pyimagesearch.com/2016/10/31/detecting-multiple-bright-spots-in-an-image-with-python-and-opencv/

    gray.CopyTo(masked); // Don't filter out red since we draw circles over it now

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
        point.ImageX[i] = minMax.MaxLoc.X;
        point.ImageY[i] = minMax.MaxLoc.Y;
        point.ImageWeight[i] = minMax.MaxVal;
    }

    if (point.ImageWeight.All(w => w == 0))
    {
        throw new Exception("Found an LED with no detectable position from any angle.");
    }

    // TODO: Check the Y values, and discount any that aren't close to the average.
    // Work out the (unweighted) average
    var averageY = point.ImageY.Sum() / point.ImageY.Where(y => y != 0).Count();


    var iyOrderOfDeltaFromAvg = Enumerable.Range(0, point.ImageY.Length)
                                          .OrderByDescending(i => Math.Abs(point.ImageY[i] - averageY))
                                          .Select(i => i);

    foreach(var iyIndex in iyOrderOfDeltaFromAvg)
    {
        if (point.ImageWeight[iyIndex] == 0) continue; // We've already discounted this one, so just carry on
        // Always have at least 4 points
        if (point.ImageWeight.Count(w => w != 0) <= MinPointsToKeep) break;
        if (Math.Abs(point.ImageY[iyIndex] - averageY) <= MaxYDiffBetweenTreeRotations) break; // If we're within 20 of the average that's fine
        point.ImageWeight[iyIndex] = 0;
        // TODO: Maybe have the routine circle the found pixel now, so we can choose not to here

        // TODO: Maybe another way of doing this is when equation solving, try removing some points to see if we get a much closer solution to the equations

    }

    // Make the weights add up to one
    var weightsSum = point.ImageWeight.Sum();

    for (var i = 0; i < point.ImageWeight.Length; i++)
    {
        point.ImageWeight[i] /= weightsSum;
    }
}

// Find the average Ys
foreach(var point in Points)
{
    Console.Write($"\nSet average Ys. {point.index} of {Points.Length}");
    point.TreeZ = point.WeightedAverageYs();
}

// Adjust all x values so the origin is the average of Xs in all images in a given tree rotation
for (int i = 0; i < Points[0].ImageX.Length; i++)
{
    var averageX = Points.Average(p => p.ImageX[i] * p.ImageWeight[i]);

    foreach (var point in Points)
    {
        point.ImageX[i] -= averageX;
    }
}

// Adjust all the Y values so the origin is the max Y (bottom of image as origin in OpenCV is top left) and +ve y's go up
var maxY = Points.Max(p => p.TreeZ);
foreach (var point in Points)
{
    point.TreeZ = maxY - point.TreeZ;
}

// Solve the equations to go from x position on image and tree rotation, an r and theta (radius and angle?)
foreach (var point in Points)
{
    Console.Write($"\rSolve equations. {point.index} of {Points.Length}");
    var weightedEquations = point.Equations.Zip(point.ImageWeight)
                                     .Select(pair => new { Equation = pair.First, Weight = pair.Second })
                                     .Where(pair => pair.Weight != 0) // Ignore equations with a weighting of zero
                                     .ToArray();

    // Just try doing it in one big list of equations (except where weighting is zero). Note that this doesn't use the weighting beyond that though
    double[] AllFuncsToSolve(double[] variables) => weightedEquations.Select(we => we.Equation(variables[0], variables[1])).ToArray();

    (var solutionsAll, var bestEquationResultsAll, var infoAll) = Fsolve.Fsolver(AllFuncsToSolve, 2, [400.0, 0.0], 1e-10);

    point.r = solutionsAll[0];
    point.theta = solutionsAll[1];
    point.EquationSolverDelta = bestEquationResultsAll.Select(Math.Abs).Average();
}


// Calculate the Tree Coords of each led
foreach (var point in Points)
{
    point.TreeX = point.r!.Value * Math.Cos(point.theta!.Value);
    point.TreeY = point.r!.Value * Math.Sin(point.theta!.Value);
    // Point.TreeZ already set
}


// TODO: Iterate through all points to work out what ones are obviously wrong, given the average distance between LEDs and divide them between probably ok LEDs
// Find distance between points
for (int i = 0; i < Points.Length - 1; i++)
{
    Points[i+1].DistanceBefore = Points[i].DistanceAfter =
        Math.Sqrt((Points[i].TreeX - Points[i + 1].TreeX) * (Points[i].TreeX - Points[i + 1].TreeX) +
                  (Points[i].TreeY - Points[i + 1].TreeY) * (Points[i].TreeY - Points[i + 1].TreeY) +
                  (Points[i].TreeZ - Points[i + 1].TreeZ) * (Points[i].TreeZ - Points[i + 1].TreeZ));
}


var probablyCorrectDistancePixels = Points.OrderBy(p => p.DistanceBefore).Take((int)(LEDCount * proportionToAssumeCorrectDistances)).ToArray();
var averageDistance = probablyCorrectDistancePixels.Average(p => p.DistanceBefore);
var maxSeparation = averageDistance / 0.75; // 3/4 is the average distance within a unit sphere

foreach (var point in Points)
{
    point.DistanceAboveThreshold = point.DistanceAfter > maxSeparation || point.DistanceBefore > maxSeparation;
}

var nextGoodIndex = 0;
var currentIndex = 0;

if (Points[currentIndex].DistanceAboveThreshold)
{ // First point is wrong, so make it and subsequent wrong ones the same as the first correct one
    while (Points[nextGoodIndex].DistanceAboveThreshold) nextGoodIndex++;
    for (; currentIndex < nextGoodIndex; currentIndex++)
    {
        Points[currentIndex].TreeX = Points[nextGoodIndex].TreeX;
        Points[currentIndex].TreeY = Points[nextGoodIndex].TreeY;
        Points[currentIndex].TreeZ = Points[nextGoodIndex].TreeZ;
    }
}

var nextGoodIndexBackwards = Points.Length - 1;
var currentIndexBackwards = Points.Length - 1;
if (Points[currentIndexBackwards].DistanceAboveThreshold)
{ // Last point is wrong, so make it and subsequent wrong ones the same as the first previous correct one
    while (Points[nextGoodIndexBackwards].DistanceAboveThreshold) nextGoodIndexBackwards--;
    for (; currentIndexBackwards > nextGoodIndexBackwards; currentIndexBackwards--)
    {
        Points[currentIndexBackwards].TreeX = Points[nextGoodIndexBackwards].TreeX;
        Points[currentIndexBackwards].TreeY = Points[nextGoodIndexBackwards].TreeY;
        Points[currentIndexBackwards].TreeZ = Points[nextGoodIndexBackwards].TreeZ;
    }
}

// Now go from nextGoodIndex to nextGoodIndexBackwards to do the rest
for (; currentIndex < currentIndexBackwards; currentIndex++)
{
    if (Points[currentIndex].DistanceAboveThreshold)
    {
        nextGoodIndex = currentIndex;
        while (Points[nextGoodIndex].DistanceAboveThreshold) nextGoodIndex++;

        var previousGoodIndex = currentIndex - 1;

        var xDiffStep = (Points[nextGoodIndex].TreeX - Points[previousGoodIndex].TreeX) / (nextGoodIndex - previousGoodIndex);
        var yDiffStep = (Points[nextGoodIndex].TreeY - Points[previousGoodIndex].TreeY) / (nextGoodIndex - previousGoodIndex);
        var zDiffStep = (Points[nextGoodIndex].TreeZ - Points[previousGoodIndex].TreeZ) / (nextGoodIndex - previousGoodIndex);

        for (; currentIndex < nextGoodIndex; currentIndex++)
        {
            Points[currentIndex].TreeX = Points[previousGoodIndex].TreeX + (currentIndex - previousGoodIndex) * xDiffStep;
            Points[currentIndex].TreeY = Points[previousGoodIndex].TreeY + (currentIndex - previousGoodIndex) * yDiffStep;
            Points[currentIndex].TreeZ = Points[previousGoodIndex].TreeZ + (currentIndex - previousGoodIndex) * zDiffStep;
        }

        currentIndex = nextGoodIndex;
    }
}



// Convert to GIFT coordinates - Make X and Y go from -ve 1 to 1, and Z go from 0 up (using the scale of the max of X and Y_
var xMin = Points.Min(p => p.TreeX);
var xMax = Points.Max(p => p.TreeX);
var yMin = Points.Min(p => p.TreeY);
var yMax = Points.Max(p => p.TreeY);
var zMin = Points.Min(p => p.TreeZ);

foreach (var point in Points)
{
    point.GiftX = (point.TreeX - xMin) / ((xMax - xMin) / 2) - 1;
    point.GiftY = (point.TreeY - yMin) / ((yMax - yMin) / 2) - 1;
    point.GiftZ = (point.TreeZ - zMin) / (Math.Max(xMax - xMin, yMax - yMin));
}


// Now write the csv
File.WriteAllLines("coordinates.csv", new[] { "index, x, y, z, r, theta, equdelta" }
    .Concat(Points.Select(p => $"{p.index}, {p.GiftX}, {p.GiftY}, {p.GiftZ}, {p.r}, {p.theta}, {p.EquationSolverDelta}")));


Console.WriteLine();
Console.WriteLine($"Done, with an equation solver delta sum of {Points.Sum(p => p.EquationSolverDelta)} and average of {Points.Average(p => p.EquationSolverDelta)}");



// Write out an Image with all calculated LED coordinates
// Cheat by starting with the first captured image and use that format
using var image = Cv2.ImRead(Points[0].imagepath[0]);
// Clear the image
image.SetTo(Scalar.Black);
// Draw the circles
foreach (var point in Points)
{
    image.Circle(image.Width / 2 + (int)Math.Round(point.GiftX!.Value * (image.Width / 4)), image.Height - (int)Math.Round(point.GiftZ!.Value * image.Height/2), 10, Scalar.All(255 - 255 * (double)point.index / Points.Length), 4);
    image.Circle(image.Width / 2 + (int)Math.Round(point.GiftX!.Value * (image.Width / 4)), image.Height - (int)Math.Round(point.GiftZ!.Value * image.Height/2), 10, point.DistanceAboveThreshold ? Scalar.Red : Scalar.Green, -1);
}

Cv2.PutText(image, "X,Z", new OpenCvSharp.Point(0, image.Height / 8), HersheyFonts.HersheyPlain, 3, Scalar.Red, 3, LineTypes.AntiAlias, false);

Cv2.ImWrite("Trees_X.png", image);

using (new Window("dst image", image, WindowFlags.AutoSize))
{
    Cv2.WaitKey();
}

// Clear the image
image.SetTo(Scalar.Black);
// Draw the circles
foreach (var point in Points)
{
    image.Circle(image.Width / 2 + (int)Math.Round(point.GiftY!.Value * (image.Width / 4)), image.Height - (int)Math.Round(point.GiftZ!.Value * image.Height/2), 10, Scalar.All(255 - 255 * (double)point.index / Points.Length), 4);
    image.Circle(image.Width / 2 + (int)Math.Round(point.GiftY!.Value * (image.Width / 4)), image.Height - (int)Math.Round(point.GiftZ!.Value * image.Height/2), 10, point.DistanceAboveThreshold ? Scalar.Red : Scalar.Green, -1);
}

Cv2.PutText(image, "Y,Z", new OpenCvSharp.Point(0, image.Height / 8), HersheyFonts.HersheyPlain, 3, Scalar.Red, 3, LineTypes.AntiAlias, false);
Cv2.ImWrite("Trees_Y.png", image);

using (new Window("dst image", image, WindowFlags.AutoSize))
{
    Cv2.WaitKey();
}

// Clear the image
image.SetTo(Scalar.Black);
// Draw the circles
foreach (var point in Points)
{
    image.Circle(image.Width / 2 + (int)Math.Round(point.GiftX!.Value * (image.Width / 4)), image.Height / 2 - (int)Math.Round(point.GiftY!.Value * (image.Height / 4)), 10, Scalar.All(255 - 255 * (double)point.index / Points.Length), 4);
    image.Circle(image.Width / 2 + (int)Math.Round(point.GiftX!.Value * (image.Width / 4)), image.Height / 2 - (int)Math.Round(point.GiftY!.Value * (image.Height / 4)), 10, point.DistanceAboveThreshold ? Scalar.Red : Scalar.Green, -1);
}

Cv2.PutText(image, "X,Y", new OpenCvSharp.Point(0, image.Height / 8), HersheyFonts.HersheyPlain, 3, Scalar.Red, 3, LineTypes.AntiAlias, false);
Cv2.ImWrite("Trees_Z.png", image);

using (new Window("dst image", image, WindowFlags.AutoSize))
{
    Cv2.WaitKey();
}


public record MinMaxLocResult(double MinVal, double MaxVal, OpenCvSharp.Point MinLoc, OpenCvSharp.Point MaxLoc, OpenCvSharp.Point ImageSize);
public class Point
{
    private const int TreeRotations = 8;
    private const string ImageBasePath = "..\\..\\..\\..\\XmasTreeLightsTo3DCaptureImages\\bin\\Debug\\net8.0";
    public int index;
    public double? r;
    public double? theta;
    public string[] imagepath = new string[8];

    // the positions to the x values
    public double[] ImageX = new double[TreeRotations];
    // the positon to the y values
    public double[] ImageY = new double[TreeRotations];
    // the the weights
    public double[] ImageWeight = new double[TreeRotations];

    public double EquationSolverDelta;
    public Func<double, double, double>[] Equations = new Func<double, double, double>[TreeRotations];

    public double TreeX;
    public double TreeY;
    public double TreeZ;

    public double DistanceBefore;
    public double DistanceAfter;
    public bool DistanceAboveThreshold;

    public double? GiftX;
    public double? GiftY;
    public double? GiftZ;

    public Point(int i)
    {
        index = i;
        imagepath = Enumerable.Range(0, Equations.Length).Select(j => Path.Join(ImageBasePath, $"{index}_{j * 45}.png")).ToArray();

        var fullRotation = Math.PI * 2;
        var rotateAngle = fullRotation / TreeRotations;

        // TODO: These are written out explicitly as i'm not sure about closures if I do it in a loop; once working could try it
        Equations[0] = (r, theta) => r * Math.Cos(theta + 0 * rotateAngle) - ImageX[0];
        Equations[1] = (r, theta) => r * Math.Cos(theta + 1 * rotateAngle) - ImageX[1];
        Equations[2] = (r, theta) => r * Math.Cos(theta + 2 * rotateAngle) - ImageX[2];
        Equations[3] = (r, theta) => r * Math.Cos(theta + 3 * rotateAngle) - ImageX[3];
        Equations[4] = (r, theta) => r * Math.Cos(theta + 4 * rotateAngle) - ImageX[4];
        Equations[5] = (r, theta) => r * Math.Cos(theta + 5 * rotateAngle) - ImageX[5];
        Equations[6] = (r, theta) => r * Math.Cos(theta + 6 * rotateAngle) - ImageX[6];
        Equations[7] = (r, theta) => r * Math.Cos(theta + 7 * rotateAngle) - ImageX[7];
    }


    public override string ToString()
    {
        return $"LED #{index}. {string.Join(", ", Enumerable.Zip(ImageX, ImageY, ImageWeight).Select(z => $"({z.First:0.00}, {z.Second:0.00}, {z.Third:0.00})"))}";
    }

    public double WeightedAverageYs()
    {
        return ImageY.Zip(ImageWeight).Sum(pair => pair.First * pair.Second);
    }

    public double WeightedAverageXs()
    {
        return ImageX.Zip(ImageWeight).Sum(pair => pair.First * pair.Second);
    }
}