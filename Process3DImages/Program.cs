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

    // Roughly blank out the left and right of the image, that isn't the tree, but gets lit up sometimes
    Cv2.Rectangle(gray, new Rect(0, 0, 100, gray.Height), new Scalar(0), -1);
    Cv2.Rectangle(gray, new Rect(500, 0, gray.Width - 500, gray.Height), new Scalar(0), -1);

    // TODO: Use ideas in here maybe: https://pyimagesearch.com/2016/10/31/detecting-multiple-bright-spots-in-an-image-with-python-and-opencv/

    gray.CopyTo(masked);

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
    point.OriginalTreeZ = point.WeightedAverageYs();
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
var maxY = Points.Max(p => p.OriginalTreeZ);
foreach (var point in Points)
{
    point.OriginalTreeZ = maxY - point.OriginalTreeZ;
}


// Func to return combinations of the items passed in with zero, one or up to X items removed
IEnumerable<IEnumerable<T>> CombinationsWithUpToXRemoved<T>(IEnumerable<T> items, int maxItemsToRemove)
{
    // Return the collection with nothing removed
    yield return items;
    if (maxItemsToRemove <= 0) yield break;

    for (int i = 0; i < items.Count(); i++)
    {
        // Loop through the collection, recursing with one each one removed
        foreach (var newItems in CombinationsWithUpToXRemoved(items.Where((item, index) => index != i), maxItemsToRemove - 1))
        {
            yield return newItems;
        }
    }
}


// Solve the equations to go from x position on image and tree rotation, an r and theta (radius and angle?)
foreach (var point in Points)
{
    Console.Write($"\rSolve equations. {point.index} of {Points.Length}");
    var weightedEquations = point.Equations.Zip(point.ImageWeight)
                                     .Select(pair => new { Equation = pair.First, Weight = pair.Second })
                                     .Where(pair => pair.Weight != 0) // Ignore equations with a weighting of zero
                                     .ToArray();

    List<Tuple<double[], double[], string>> possibleSolutions = [];

    //var maxToRemove = weightedEquations.Length - 4; // Remove down to a minimum of 4 equations
    var maxToRemove = 0; // Don't remove any equations, just use them all (as it doesn't seem to make much difference we just leave it at none

    foreach (var equationCombinations in CombinationsWithUpToXRemoved(weightedEquations, maxToRemove))
    {
        // Just try doing it in one big list of equations (except where weighting is zero). Note that this doesn't use the weighting beyond that though
        double[] AllFuncsToSolve(double[] variables) => weightedEquations.Select(we => we.Equation(variables[0], variables[1])).ToArray();

        possibleSolutions.Add(Fsolve.Fsolver(AllFuncsToSolve, 2, [400.0, 0.0], 1e-10));
    }

    // Find the combination of equations that gives the lowest delta, and use that solution
    (var solutionsAll, var bestEquationResultsAll, var infoAll) = possibleSolutions.OrderBy(result => result.Item2.Select(Math.Abs).Average()).First();

    point.r = solutionsAll[0];
    point.theta = solutionsAll[1];
    point.EquationSolverDelta = bestEquationResultsAll.Select(Math.Abs).Average();
}


// Calculate the Tree Coords of each led
foreach (var point in Points)
{
    point.OriginalTreeX = point.r!.Value * Math.Cos(point.theta!.Value);
    point.OriginalTreeY = point.r!.Value * Math.Sin(point.theta!.Value);
    // Point.TreeZ already set
}


// TODO: Iterate through all points to work out what ones are obviously wrong, given the average distance between LEDs and divide them between probably ok LEDs
// Find distance between points
for (int i = 0; i < Points.Length - 1; i++)
{
    Points[i+1].DistanceBefore = Points[i].DistanceAfter =
        Math.Sqrt((Points[i].OriginalTreeX - Points[i + 1].OriginalTreeX) * (Points[i].OriginalTreeX - Points[i + 1].OriginalTreeX) +
                  (Points[i].OriginalTreeY - Points[i + 1].OriginalTreeY) * (Points[i].OriginalTreeY - Points[i + 1].OriginalTreeY) +
                  (Points[i].OriginalTreeZ - Points[i + 1].OriginalTreeZ) * (Points[i].OriginalTreeZ - Points[i + 1].OriginalTreeZ));
}


var probablyCorrectDistancePixels = Points.OrderBy(p => p.DistanceBefore).Take((int)(LEDCount * proportionToAssumeCorrectDistances)).ToArray();
var averageDistance = probablyCorrectDistancePixels.Average(p => p.DistanceBefore);
var maxSeparation = averageDistance / 0.75; // 3/4 is the average distance within a unit sphere

foreach (var point in Points)
{
    point.DistanceAboveThreshold = point.DistanceAfter > maxSeparation || point.DistanceBefore > maxSeparation;
    // Start of with them being the same
    point.CorrectedTreeX = point.OriginalTreeX;
    point.CorrectedTreeY = point.OriginalTreeY;
    point.CorrectedTreeZ = point.OriginalTreeZ;
}

// Go through and mark for correction any where before it and after it are to be corrected
for (int i = 1; i < Points.Length - 1; i++)
{
    if (Points[i - 1].DistanceAboveThreshold && Points[i + 1].DistanceAboveThreshold)
    {
        Points[i].DistanceAboveThreshold = true;
    }
}


var nextGoodIndex = 0;
var currentIndex = 0;

if (Points[currentIndex].DistanceAboveThreshold)
{ // First point is wrong, so make it and subsequent wrong ones the same as the first correct one
    while (Points[nextGoodIndex].DistanceAboveThreshold) nextGoodIndex++;
    for (; currentIndex < nextGoodIndex; currentIndex++)
    {
        Points[currentIndex].CorrectedTreeX = Points[nextGoodIndex].CorrectedTreeX;
        Points[currentIndex].CorrectedTreeY = Points[nextGoodIndex].CorrectedTreeY;
        Points[currentIndex].CorrectedTreeZ = Points[nextGoodIndex].CorrectedTreeZ;
    }
}

var nextGoodIndexBackwards = Points.Length - 1;
var currentIndexBackwards = Points.Length - 1;
if (Points[currentIndexBackwards].DistanceAboveThreshold)
{ // Last point is wrong, so make it and subsequent wrong ones the same as the first previous correct one
    while (Points[nextGoodIndexBackwards].DistanceAboveThreshold) nextGoodIndexBackwards--;
    for (; currentIndexBackwards > nextGoodIndexBackwards; currentIndexBackwards--)
    {
        Points[currentIndexBackwards].CorrectedTreeX = Points[nextGoodIndexBackwards].CorrectedTreeX;
        Points[currentIndexBackwards].CorrectedTreeY = Points[nextGoodIndexBackwards].CorrectedTreeY;
        Points[currentIndexBackwards].CorrectedTreeZ = Points[nextGoodIndexBackwards].CorrectedTreeZ;
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

        var xDiffStep = (Points[nextGoodIndex].CorrectedTreeX - Points[previousGoodIndex].CorrectedTreeX) / (nextGoodIndex - previousGoodIndex);
        var yDiffStep = (Points[nextGoodIndex].CorrectedTreeY - Points[previousGoodIndex].CorrectedTreeY) / (nextGoodIndex - previousGoodIndex);
        var zDiffStep = (Points[nextGoodIndex].CorrectedTreeZ - Points[previousGoodIndex].CorrectedTreeZ) / (nextGoodIndex - previousGoodIndex);

        for (; currentIndex < nextGoodIndex; currentIndex++)
        {
            Points[currentIndex].CorrectedTreeX = Points[previousGoodIndex].CorrectedTreeX + (currentIndex - previousGoodIndex) * xDiffStep;
            Points[currentIndex].CorrectedTreeY = Points[previousGoodIndex].CorrectedTreeY + (currentIndex - previousGoodIndex) * yDiffStep;
            Points[currentIndex].CorrectedTreeZ = Points[previousGoodIndex].CorrectedTreeZ + (currentIndex - previousGoodIndex) * zDiffStep;
        }

        currentIndex = nextGoodIndex;
    }
}



// Convert to GIFT coordinates - Make X and Y go from -ve 1 to 1, and Z go from 0 up (using the scale of the max of X and Y_
var xMin = Points.Min(p => p.CorrectedTreeX);
var xMax = Points.Max(p => p.CorrectedTreeX);
var yMin = Points.Min(p => p.CorrectedTreeY);
var yMax = Points.Max(p => p.CorrectedTreeY);
var zMin = Points.Min(p => p.CorrectedTreeZ);

foreach (var point in Points)
{
    point.GiftX = (point.CorrectedTreeX - xMin) / ((xMax - xMin) / 2) - 1;
    point.GiftY = (point.CorrectedTreeY - yMin) / ((yMax - yMin) / 2) - 1;
    point.GiftZ = (point.CorrectedTreeZ - zMin) / (Math.Max(xMax - xMin, yMax - yMin)); // TODO: Correct his so the tree isn't squashed in the Z direction
}


// Now write the csv
File.WriteAllLines("coordinates.csv", new[] { "index, x, y, z, r, theta, equdelta, wascorrected, original x, original y, original z" }
    .Concat(Points.Select(p => $"{p.index}, {p.GiftX}, {p.GiftY}, {p.GiftZ}, {p.r}, {p.theta}, {p.EquationSolverDelta}, {p.DistanceAboveThreshold}, {p.OriginalTreeX}, {p.OriginalTreeY}, {p.OriginalTreeZ}")));


Console.WriteLine();
Console.WriteLine($"Found points to correct with indexes: {string.Join(", ", Points.Where(p => p.DistanceAboveThreshold).Select(p => p.index))}");
Console.WriteLine($"Done, with {Points.Count(p => !p.DistanceAboveThreshold) / (double)Points.Count():P2} probably correct points and an equation solver delta sum of {Points.Sum(p => p.EquationSolverDelta)} and average of {Points.Average(p => p.EquationSolverDelta)}");



// Write out an Image with all calculated LED coordinates
// Cheat by starting with the first captured image and use that format
using var original = Cv2.ImRead(Points[0].imagepath[0]);
using var image = original.Resize(new Size(original.Width * 2, original.Height * 2));
// Clear the image
image.SetTo(Scalar.Black);

var circleR = 10;
// Draw the circles
foreach (var point in Points)
{
    var circleX = image.Width / 2 + (int)Math.Round(point.GiftX!.Value * (image.Width / 4));
    var circleY = image.Height - (int)Math.Round(point.GiftZ!.Value * image.Height / 2);
    image.Circle(circleX, circleY, circleR * 2, Scalar.All(255 - 255 * (double)point.index / Points.Length), 4);
    image.Circle(circleX, circleY, circleR * 2, point.DistanceAboveThreshold ? Scalar.Red : Scalar.Green, -1);

    var textSize = Cv2.GetTextSize(point.index.ToString(), HersheyFonts.HersheyPlain, 1, 1, out _);
    image.PutText(point.index.ToString(), new OpenCvSharp.Point(circleX - textSize.Width / 2, circleY - textSize.Height / 2), HersheyFonts.HersheyPlain, 1, Scalar.White, 1);
}

Cv2.PutText(image, "X,Z", new OpenCvSharp.Point(0, image.Height / 8), HersheyFonts.HersheyPlain, 3, Scalar.Red, 3, LineTypes.AntiAlias, false);

Cv2.ImWrite("Trees_X.png", image);


using (new Window("X Z Image", image, WindowFlags.AutoSize))
{
    Cv2.WaitKey();
}

// Clear the image
image.SetTo(Scalar.Black);
// Draw the circles
foreach (var point in Points)
{
    var circleX = image.Width / 2 + (int)Math.Round(point.GiftY!.Value * (image.Width / 4));
    var circleY = image.Height - (int)Math.Round(point.GiftZ!.Value * image.Height / 2);
    image.Circle(circleX, circleY, circleR * 2, Scalar.All(255 - 255 * (double)point.index / Points.Length), 4);
    image.Circle(circleX, circleY, circleR * 2, point.DistanceAboveThreshold ? Scalar.Red : Scalar.Green, -1);

    var textSize = Cv2.GetTextSize(point.index.ToString(), HersheyFonts.HersheyPlain, 1, 1, out _);
    image.PutText(point.index.ToString(), new OpenCvSharp.Point(circleX - textSize.Width / 2, circleY - textSize.Height / 2), HersheyFonts.HersheyPlain, 1, Scalar.White, 1);
}

Cv2.PutText(image, "Y,Z", new OpenCvSharp.Point(0, image.Height / 8), HersheyFonts.HersheyPlain, 3, Scalar.Red, 3, LineTypes.AntiAlias, false);
Cv2.ImWrite("Trees_Y.png", image);

using (new Window("Y Z Image", image, WindowFlags.AutoSize))
{
    Cv2.WaitKey();
}

// Clear the image
image.SetTo(Scalar.Black);
// Draw the circles
foreach (var point in Points)
{
    var circleX = image.Width / 2 + (int)Math.Round(point.GiftX!.Value * (image.Width / 4));
    var circleY = image.Height / 2 - (int)Math.Round(point.GiftY!.Value * (image.Height / 4));
    image.Circle(circleX, circleY, circleR * 2, Scalar.All(255 - 255 * (double)point.index / Points.Length), 4);
    image.Circle(circleX, circleY, circleR * 2, point.DistanceAboveThreshold ? Scalar.Red : Scalar.Green, -1);

    var textSize = Cv2.GetTextSize(point.index.ToString(), HersheyFonts.HersheyPlain, 1, 1, out _);
    image.PutText(point.index.ToString(), new OpenCvSharp.Point(circleX - textSize.Width / 2, circleY - textSize.Height / 2), HersheyFonts.HersheyPlain, 1, Scalar.White, 1);
}

Cv2.PutText(image, "X,Y", new OpenCvSharp.Point(0, image.Height / 8), HersheyFonts.HersheyPlain, 3, Scalar.Red, 3, LineTypes.AntiAlias, false);
Cv2.ImWrite("Trees_Z.png", image);

using (new Window("X Y Image", image, WindowFlags.AutoSize))
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

    public double CorrectedTreeX;
    public double CorrectedTreeY;
    public double CorrectedTreeZ;

    public double OriginalTreeX;
    public double OriginalTreeY;
    public double OriginalTreeZ;

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