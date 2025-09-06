namespace TreeLightsWeb.ImageProcessing
{
    public record MinMaxLocResult(double MinVal, double MaxVal, OpenCvSharp.Point MinLoc, OpenCvSharp.Point MaxLoc, OpenCvSharp.Point ImageSize);
}
