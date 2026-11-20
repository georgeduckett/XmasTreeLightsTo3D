namespace TreeLightsWeb.Models
{
    public class ImageProcessingModel
    {
        public record ImageMaskingModel(int LeftBlank, int RightBlank);
        public string? WebRootFolder { get; set; }
        public int LEDStart { get; set; }
        public int LEDCount { get; set; }
        // Optional, per direction: blank out the image to the left of LeftBlank and right of RightBlank (e.g. a window
        // or TV the lights reflect in). If they'd cut off much of the tree they're ignored and the tree is found automatically.
        public ImageMaskingModel[] ImageMaskingModels { get; set; } = Enumerable.Range(0, 8).Select(_ => new ImageMaskingModel(0, 0)).ToArray();
        // Find the LEDs in the images again, even if the images haven't changed since they were last processed
        public bool RecalculateImageLEDCoordinates { get; set; } = false;
        public int CameraDelayMilliseconds { get; set; } = 200;
    }
}
