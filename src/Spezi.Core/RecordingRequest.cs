namespace Spezi.Core;

public sealed class RecordingRequest
{
    public required AppSettings Settings { get; init; }
    public required CaptureSelection Capture { get; init; }
    public TimeSpan ReplayDuration { get; set; }
    public string? GameTitle { get; init; }
}
