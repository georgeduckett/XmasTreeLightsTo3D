namespace WLEDInterface
{
    public record ThreeDPoint(double X, double Y, double Z)
    {
        public ThreeDPoint(double[] points) : this(points[1], points[2], points[3]) { }
    }
}