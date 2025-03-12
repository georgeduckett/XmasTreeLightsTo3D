namespace TreeLightsWeb.Models
{
    public class ImageProcessingModel
    {
        public record ImageMaskingModel(int LeftBlank, int RightBlank);
        public string? WebRootFolder { get; set; }
        public int LEDCount { get; set; }
        public double BlurAmount { get; set; }
        public double BrightnessThreshold { get; set; } // A point must be at least this bright to be considered the brightest point
        public int MinPointsToKeep { get; set; } // When considering points across tree rotations, keep at least this many no matter how differnt the Y image values
        public int MaxYDiffBetweenTreeRotations { get; set; } // When considering an LED's image Y values accross tree rotations, disgard any different too from the average
        public double ProportionToAssumeCorrectDistances { get; set; } // Proportion of distances between LEDs to consider to be correct
        public ImageMaskingModel[]? ImageMaskingModels { get; set; }
    }
}
