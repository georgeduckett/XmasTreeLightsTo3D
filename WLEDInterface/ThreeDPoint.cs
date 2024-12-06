namespace WLEDInterface
{
    public record ThreeDPoint(double X, double Y, double Z)
    {
        public ThreeDPoint(double[] points) : this(points[0], points[2], points[3])
        {
            if (points.Length == 7)
            { // If we have 7 figures then it's `index, x, y, z, r, theta, equdelta`, so we use indexes 1, 2 and 3 as the x, y and z coords
                X = points[1];
                Y = points[2];
                Z = points[3];
            }
        }
    }
}
