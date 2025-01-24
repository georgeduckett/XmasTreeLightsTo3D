namespace TreeLightsWeb.Models
{
    public class ImageProcessingModel
    {
        public double BlurAmount { get; set; }
        public double BrightnessThreshold { get; set; }
        public int MinPointsToKeep { get; set; }
        public int MaxYDiffBetweenTreeRotations { get; set; }
        public double ProportionToAssumeCorrectDistances { get; set; }
    }
}
