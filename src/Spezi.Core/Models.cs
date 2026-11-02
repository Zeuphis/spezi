namespace Spezi.Core;

public enum QualityProfile
{
    Economy = 0,
    Balanced = 1,
    High = 2,
    Custom = 3
}

public enum CaptureKind
{
    Monitor = 0,
    Window = 1
}

public enum AudioCaptureMode
{
    System = 0,
    Applications = 1
}

public sealed class HotkeyBinding
{
    public uint Modifiers { get; set; }
    public int VirtualKey { get; set; }

    public HotkeyBinding()
    {
    }

    public HotkeyBinding(uint modifiers, int virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    public bool IsValid => VirtualKey != 0;

    public bool ConflictsWith(HotkeyBinding other) =>
        other.IsValid && Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;
}

public sealed class AppSettings
{
    public bool SetupCompleted { get; set; }
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Spezi");
    public int ReplaySeconds { get; set; } = 180;
    /// <summary>Null keeps the previous default: system audio stays on.</summary>
    public bool? CaptureSystemAudio { get; set; }
    public AudioCaptureMode AudioMode { get; set; }
    public List<string> AudioApplicationIds { get; set; } = [AudioApplicationEntry.GameId];
    public HotkeyBinding ReplayHotkey { get; set; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x77); // F8
    public HotkeyBinding RecordHotkey { get; set; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x78); // F9
    public QualityProfile Profile { get; set; } = QualityProfile.Balanced;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int FrameRate { get; set; } = 60;
    public int VideoBitrateKbps { get; set; } = 15000;
    public bool AllowSoftwareEncoder { get; set; }
    public bool CaptureCursor { get; set; } = true;
    public List<string> OutputDeviceIds { get; set; } = [];
    public bool RecordMicrophone { get; set; }
    public string? MicrophoneId { get; set; }
    public Dictionary<string, double> DeviceGains { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool StartWithWindows { get; set; }
    public bool AutoReplayWhenPlaying { get; set; }
    public bool StatusToasts { get; set; } = true;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public double ToastDurationSeconds { get; set; } = 3.5;
    public const string DefaultAccentColor = "#5CDBA6";
    public string AccentColor { get; set; } = DefaultAccentColor;
    public string MonitorDeviceName { get; set; } = "primary";

    public double GainFor(string deviceId)
    {
        if (DeviceGains.TryGetValue(deviceId, out var gain))
            return Math.Clamp(gain, 0, 2);
        return 1;
    }

    public void Clamp()
    {
        ReplaySeconds = ValidReplaySeconds(ReplaySeconds);
        if (!Enum.IsDefined(AudioMode)) AudioMode = AudioCaptureMode.System;
        AudioApplicationIds = (AudioApplicationIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
        ToastDurationSeconds = ValidToastDuration(ToastDurationSeconds);
        AccentColor = ValidAccentColor(AccentColor);
        Width = MakeEven(Math.Clamp(Width, 640, 3840));
        Height = MakeEven(Math.Clamp(Height, 360, 2160));
        FrameRate = FrameRate is 30 or 60 or 120 ? FrameRate : 60;
        VideoBitrateKbps = Math.Clamp(VideoBitrateKbps, 2000, 80000);
        if (string.IsNullOrWhiteSpace(OutputDirectory))
            OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Spezi");
    }

    public static int MakeEven(int value) => Math.Max(2, value & ~1);

    public static int ValidReplaySeconds(int value) => (int)Math.Round(Math.Clamp(value, 15, 300) / 15d, MidpointRounding.AwayFromZero) * 15;

    public static string ReplayMemoryText(int seconds, int bitrateKbps)
    {
        var mb = Recording.ReplayBuffer.MemoryLimitBytes(TimeSpan.FromSeconds(seconds), bitrateKbps) / (1024d * 1024);
        return mb >= 1024 ? $"Uses up to about {mb / 1024:0.0} GB of RAM at {bitrateKbps / 1000d:0.#} Mbit/s." : $"Uses up to about {mb:0} MB of RAM at {bitrateKbps / 1000d:0.#} Mbit/s.";
    }

    public static string ReplayDurationText(int seconds) => seconds < 60 ? $"{seconds}s" : seconds % 60 == 0
        ? $"{seconds / 60}m" : $"{seconds / 60}m {seconds % 60}s";

    public static double ValidToastDuration(double value) => double.IsFinite(value) ? Math.Clamp(value, 1, 15) : 3.5;

    public static string ValidAccentColor(string? value)
    {
        var color = value?.Trim();
        return color?.Length == 7 && color[0] == '#' && color.AsSpan(1).ToArray().All(char.IsAsciiHexDigit)
            ? color.ToUpperInvariant() : DefaultAccentColor;
    }

    public static void ApplyProfile(AppSettings settings, QualityProfile profile, int nativeWidth, int nativeHeight)
    {
        nativeWidth = MakeEven(Math.Max(2, nativeWidth));
        nativeHeight = MakeEven(Math.Max(2, nativeHeight));
        settings.Profile = profile;
        switch (profile)
        {
            case QualityProfile.Economy:
                settings.Width = 1280;
                settings.Height = 720;
                settings.FrameRate = 30;
                settings.VideoBitrateKbps = 6000;
                break;
            case QualityProfile.Balanced:
                Fit(settings, nativeWidth, nativeHeight, 1920, 1080);
                settings.FrameRate = 60;
                settings.VideoBitrateKbps = 15000;
                break;
            case QualityProfile.High:
                Fit(settings, nativeWidth, nativeHeight, 2560, 1440);
                settings.FrameRate = 60;
                settings.VideoBitrateKbps = nativeHeight > 1080 || nativeWidth > 1920 ? 28000 : 22000;
                break;
        }
    }

    static void Fit(AppSettings settings, int nativeWidth, int nativeHeight, int maxWidth, int maxHeight)
    {
        var width = nativeWidth;
        var height = nativeHeight;
        if (width > maxWidth || height > maxHeight)
        {
            var scale = Math.Min(maxWidth / (double)width, maxHeight / (double)height);
            width = MakeEven((int)Math.Round(width * scale));
            height = MakeEven((int)Math.Round(height * scale));
        }
        settings.Width = width;
        settings.Height = height;
    }
}

public static class HotkeyModifiers
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;
    public const uint NoRepeat = 0x4000;
}

public enum NoticeKind
{
    Information,
    ReplayStarted,
    ReplayStopped,
    ClipSaved,
    SessionStarted,
    SessionStopped,
    AudioDeviceLost,
    CaptureSourceLost,
    DiskLow,
    DiskFull,
    WriteAccessDenied,
    EncoderMissing,
    RecoveredRecording,
    Error
}

public sealed class RecorderNotice
{
    public required NoticeKind Kind { get; init; }
    public required string Message { get; init; }
    public string? FilePath { get; init; }
}

public sealed class RecorderSnapshot
{
    public bool PipelineRunning { get; init; }
    public bool ReplayActive { get; init; }
    public bool SessionActive { get; init; }
    public bool AudioTestActive { get; init; }
    public bool UsingSoftwareEncoder { get; init; }
    public string EncoderName { get; init; } = "";
    public TimeSpan SessionElapsed { get; init; }
    public TimeSpan ReplayBuffered { get; init; }
    public int ReplaySeconds { get; init; }
    public double CaptureFps { get; init; }
    public double EncodedFps { get; init; }
    public long DroppedFrames { get; init; }
    public long ReplayBytes { get; init; }
    public IReadOnlyList<AudioLevel> Levels { get; init; } = [];
    public string? StatusText { get; init; }
}

public sealed class AudioLevel
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public bool IsMicrophone { get; init; }
    public float Peak { get; init; }
    public bool Active { get; init; }
}

public sealed class AudioDeviceEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsMicrophone { get; init; }
    public bool IsDefault { get; init; }
}

public sealed class AudioApplicationEntry
{
    public const string GameId = "@game";
    public const string DiscordId = "@discord";
    public const string TeamSpeakId = "@teamspeak";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsRunning { get; init; }
}

public sealed class DisplayEntry
{
    public required string DeviceName { get; init; }
    public required string Name { get; init; }
    public nint MonitorHandle { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed class WindowEntry
{
    public nint Handle { get; init; }
    public required string Title { get; init; }
    public required string ProcessName { get; init; }
}

public sealed class EncoderDescription
{
    public required string Name { get; init; }
    public bool IsHardware { get; init; }
    public uint VendorId { get; init; }
    public string Vendor { get; init; } = "";
}

public sealed class SavedClip
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public DateTime CreatedUtc { get; init; }
    public long Bytes { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed class CaptureSelection
{
    public CaptureKind Kind { get; init; }
    public nint Handle { get; init; }
    public string MonitorDeviceName { get; init; } = "primary";
}

public sealed class ResourceSample
{
    public double ProcessCpuPercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public double GpuPercent { get; init; }
    public double EncoderPercent { get; init; }
}
