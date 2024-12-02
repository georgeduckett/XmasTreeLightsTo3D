using MathNet.Symbolics;
using OpenCvSharp;

var LEDCount = 400;

var Points = Enumerable.Range(0, LEDCount).Select(i => new Point(i)).ToArray();

var blurAmount = (double)41;

MinMaxLocResult ImageBP(string filePath)
{
    var image = Cv2.ImRead(filePath);
    var orig = new Mat();
    image.CopyTo(orig);
    var gray = new Mat();
    Cv2.CvtColor(orig, gray, ColorConversionCodes.BGR2GRAY);
    Cv2.MinMaxLoc(gray, out var minVal, out var maxVal, out var minLoc, out var maxLoc);
    return new MinMaxLocResult(minVal, maxVal, minLoc, maxLoc);
}


var ImageBasePath = "C:\\Users\\";

// Set all paths
foreach(var point in Points)
{
    point.image_1_path = Path.Join(ImageBasePath, $"{point.index}_0.png");
    point.image_2_path = Path.Join(ImageBasePath, $"{point.index}_45.png");
    point.image_3_path = Path.Join(ImageBasePath, $"{point.index}_90.png");
    point.image_4_path = Path.Join(ImageBasePath, $"{point.index}_135.png");
    point.image_5_path = Path.Join(ImageBasePath, $"{point.index}_180.png");
    point.image_6_path = Path.Join(ImageBasePath, $"{point.index}_225.png");
    point.image_7_path = Path.Join(ImageBasePath, $"{point.index}_270.png");
    point.image_8_path = Path.Join(ImageBasePath, $"{point.index}_315.png");
}

// Find brightest points
foreach(var point in Points)
{
    var minMax1 = ImageBP(point.image_1_path);
    var minMax2 = ImageBP(point.image_2_path);
    var minMax3 = ImageBP(point.image_3_path);
    var minMax4 = ImageBP(point.image_4_path);
    var minMax5 = ImageBP(point.image_5_path);
    var minMax6 = ImageBP(point.image_6_path);
    var minMax7 = ImageBP(point.image_7_path);
    var minMax8 = ImageBP(point.image_8_path);

    point.i1x = minMax1.MaxLoc.X;
    point.i2x = minMax2.MaxLoc.X;
    point.i3x = minMax3.MaxLoc.X;
    point.i4x = minMax4.MaxLoc.X;
    point.i5x = minMax5.MaxLoc.X;
    point.i6x = minMax6.MaxLoc.X;
    point.i7x = minMax7.MaxLoc.X;
    point.i8x = minMax8.MaxLoc.X;

    point.i1y = minMax1.MaxLoc.Y;
    point.i2y = minMax2.MaxLoc.Y;
    point.i3y = minMax3.MaxLoc.Y;
    point.i4y = minMax4.MaxLoc.Y;
    point.i5y = minMax5.MaxLoc.Y;
    point.i6y = minMax6.MaxLoc.Y;
    point.i7y = minMax7.MaxLoc.Y;
    point.i8y = minMax8.MaxLoc.Y;

    point.i1w = minMax1.MaxVal;
    point.i2w = minMax2.MaxVal;
    point.i3w = minMax3.MaxVal;
    point.i4w = minMax4.MaxVal;
    point.i5w = minMax5.MaxVal;
    point.i6w = minMax6.MaxVal;
    point.i7w = minMax7.MaxVal;
    point.i8w = minMax8.MaxVal;
}

// Find the average Ys
foreach(var point in Points)
{
    point.actualy = point.averageYs();
}

// Find the average Xs (so we can have zero as the origin of X and Z coords)
var average_xs = Points.Select(p => p.averageXs()).ToArray();

var average_x = average_xs.Sum() / (average_xs.Length * 8); // We multiply by 8 here since we don't earlier, and I'm replicating the code

// They manually change the Xs (to 300), but I'm not doing that

// Adjust all x values
foreach(var point in Points)
{
    point.i1x -= average_x;
    point.i2x -= average_x;
    point.i3x -= average_x;
    point.i4x -= average_x;
    point.i5x -= average_x;
    point.i6x -= average_x;
    point.i7x -= average_x;
    point.i8x -= average_x;
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

foreach (var point in Points)
{
    var r = Symbol.NewSymbol("r");
    var theta = Symbol.NewSymbol("theta");

    point.eqn1 = new SymbolicExpression(r )
}

public record MinMaxLocResult(double MinVal, double MaxVal, OpenCvSharp.Point MinLoc, OpenCvSharp.Point MaxLoc);
public class Point(int i)
{
    public int index = i;
    public double? r;
    public double? theta;
    public string? image_1_path;
    public string? image_2_path;
    public string? image_3_path;
    public string? image_4_path;
    public string? image_5_path;
    public string? image_6_path;
    public string? image_7_path;
    public string? image_8_path;

    public double? actualy;

    // the positions to the x values
    public double? i1x;
    public double? i2x;
    public double? i3x;
    public double? i4x;
    public double? i5x;
    public double? i6x;
    public double? i7x;
    public double? i8x;
    // the positon to the y values
    public double? i1y;
    public double? i2y;
    public double? i3y;
    public double? i4y;
    public double? i5y;
    public double? i6y;
    public double? i7y;
    public double? i8y;
    // the the weights
    public double? i1w;
    public double? i2w;
    public double? i3w;
    public double? i4w;
    public double? i5w;
    public double? i6w;
    public double? i7w;
    public double? i8w;

    // equations
    public double? eqn1;
    public double? eqn2;
    public double? eqn3;
    public double? eqn4;
    public double? eqn5;
    public double? eqn6;
    public double? eqn7;
    public double? eqn8;

    public override string ToString()
    {
        return $"the x values are: {i1x}, {i2x}, {i3x}, {i4x}, {i5x}, {i6x}, {i7x}, {i8x}, y values are: {i1y}, {i2y}, {i3y}, {i4y}, {i5y}, {i6y}, {i7y}, {i8y}, the weights are: {i1w}, {i2w}, {i3w}, {i4w}, {i5w}, {i6w}, {i7w}, {i8w}";
    }

    public double averageYs()
    {
        var y_s = new[] { i1y * i1w, i2y * i2w, i3y * i3w, i4y * i4w, i5y * i5w, i6y * i6w, i7y * i7w, i8y * i8w };
        var weights = new[] { i1w, i2w, i3w, i4w, i5w, i6w, i7w, i8w }; // TODO: Should have i8w?
        return (y_s.Sum() / weights.Sum()).Value; // TODO: Shouldn't we also divide by the number of them?? (8)
    }

    public double averageXs()
    {
        var x_s = new[] { i1x * i1w, i2x * i2w, i3x * i3w, i4x * i4w, i5x * i5w, i6x * i6w, i7x * i7w, i8x * i8w };
        var weights = new[] { i1w, i2w, i3w, i4w, i5w, i6w, i7w, i8w }; // TODO: Shouldn't we also divide by the number of them?? (8) We don't as we do that when getting average
        return (x_s.Sum() / weights.Sum()).Value;
    }
}