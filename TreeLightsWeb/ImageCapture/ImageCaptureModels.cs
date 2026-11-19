using System.Text.Json.Serialization;

namespace TreeLightsWeb.ImageCapture
{
    /// <summary>
    /// The tree rotations (in degrees) that images are captured at. Image processing expects exactly these.
    /// </summary>
    public static class CaptureDirections
    {
        public static readonly IReadOnlyList<int> All = [0, 45, 90, 135, 180, 225, 270, 315];
        public static bool IsValid(int direction) => All.Contains(direction);
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CaptureJobKind { Capture, Latency }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CaptureState { Idle, Queued, Running, Completed, Failed, Cancelled }

    // Requests from the capture page
    public record StartCaptureRequest(string ClientId, int Direction, int SettleDelayMs, int ExposureSettleMs, bool Resume,
                                      int Width, int Height, bool AllowResolutionMismatch);
    public record MeasureLatencyRequest(string ClientId);
    public record StartResult(bool Ok, string? JobId = null, string? Error = null, string? Code = null);
    public record UploadOutcome(int StatusCode, string? Error = null);

    // Messages to/from the capture page (the camera) over SignalR
    public record FrameRequest(string JobId, long Seq, string Name, int SettleDelayMs, int Done, int Total);
    public record FrameResult(bool Ok, int Width, int Height, string? Error);
    public record LatencyProbeRequest(string JobId, int WindowMs);
    public record LatencyProbeResult(bool Ok, double FirstChangeMs, double FullChangeMs, double FrameIntervalMs, double Change, string? Error);

    public record LatencyResult(int Trials, double FirstChangeMs, double FullChangeMs, double FrameIntervalMs, int SuggestedSettleDelayMs);

    /// <summary>
    /// The state of the current (or most recent) capture job, pushed to every capture page as it changes.
    /// </summary>
    public record CaptureStatus
    {
        public static readonly CaptureStatus Idle = new();

        public long Version { get; init; }
        public CaptureState State { get; init; } = CaptureState.Idle;
        public CaptureJobKind Kind { get; init; } = CaptureJobKind.Capture;
        public string? JobId { get; init; }
        public string? CameraClientId { get; init; }
        public int Direction { get; init; }
        public int Done { get; init; }
        public int Total { get; init; }
        public bool Paused { get; init; }
        public string Message { get; init; } = string.Empty;
        public double ElapsedSeconds { get; init; }
        public double? SecondsPerImage { get; init; }
        public double? EtaSeconds { get; init; }
        public LatencyResult? Latency { get; init; }
        public bool IsActive => State is CaptureState.Queued or CaptureState.Running;
    }

    /// <summary>Written alongside a capture that's in progress, so it can be resumed later.</summary>
    public record StagingManifest(int LedStart, int LedCount, int Width, int Height, DateTimeOffset Started);

    /// <summary>Written next to the images once all of a direction's images have been captured.</summary>
    public record CaptureManifest(int Direction, int LedStart, int LedCount, int Width, int Height, int SettleDelayMs,
                                  int ExposureSettleMs, DateTimeOffset Completed, double DurationSeconds);

    public record DirectionSummary(int Direction, int Images, bool Complete, DateTimeOffset? Captured, int? Width, int? Height,
                                   int StagedImages, int StagedTotal);
    public record CaptureSummary(bool TreeConnected, int? LedCount, IReadOnlyList<DirectionSummary> Directions);

    /// <summary>A problem that stops a capture, with a message that's shown to the user as-is.</summary>
    public class CaptureFailedException(string message) : Exception(message);
}
