namespace TreeLightsWeb.ImageProcessing
{
    public class Point
    {
        private const int TreeRotations = 8;
        private readonly string ImageBasePath;
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

        public Point(string imageBasePath, int i)
        {
            ImageBasePath = imageBasePath;
            index = i;
            imagepath = Enumerable.Range(0, Equations.Length).Select(j => Path.Join(ImageBasePath, $"{index}_{j * 45}.png")).ToArray();

            var fullRotation = Math.PI * 2;
            var rotateAngle = fullRotation / TreeRotations;

            // These are written out explicitly as we can't create it would create colsures if done in a loop
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
}
