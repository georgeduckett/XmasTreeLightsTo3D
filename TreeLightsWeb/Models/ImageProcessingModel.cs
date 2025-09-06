namespace TreeLightsWeb.Models
{
    public class ImageProcessingModel
    {
        public record ImageMaskingModel(int LeftBlank, int RightBlank);
        public string? WebRootFolder { get; set; }
        public int LEDCount { get; set; }
        public double BlurAmount { get; set; } = 5;
        public double BrightnessThreshold { get; set; } = 20; // A point must be at least this bright to be considered the brightest point
        public int MinPointsToKeep { get; set; } = 40; // When considering points across tree rotations, keep at least this many no matter how differnt the Y image values
        public int MaxYDiffBetweenTreeRotations { get; set; } = 20; // When considering an LED's image Y values accross tree rotations, disgard any different too from the average
        public double ProportionToAssumeCorrectDistances { get; set; } = 0.8; // Proportion of distances between LEDs to consider to be correct
        public ImageMaskingModel[] ImageMaskingModels { get; set; } = Enumerable.Range(0, 8).Select(_ => new ImageMaskingModel(0, 0)).ToArray();
        public bool RecalculateImageLEDCoordinates { get; set; } = false;
    }
}
