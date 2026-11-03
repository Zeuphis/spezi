using System.Diagnostics;
using System.Runtime.InteropServices;
using Spezi.Core;
using Spezi.Core.Recording;

namespace Spezi.App;

public sealed class AppController : IDisposable
{
    readonly Recorder _recorder = new();
    readonly DispatcherTimerShim _watch;
    readonly bool _persistSettings;
    string _libraryRoot;
    IReadOnlyList<EncoderDescription>? _encoderCache;
    long _encoderRefreshAt;
    bool _diskWarned;
    string _lastHotkeyError = "";

    public AppSettings Settings { get; private set; }
    public CaptureSelection Capture { get; private set; } = new() { Kind = CaptureKind.Monitor, MonitorDeviceName = "primary" };
    public string ReplayHotkeyError { get; private set; } = "";
    public string RecordHotkeyError { get; private set; } = "";
    // Both fields carry the same text on a conflict; show it once.
    public string HotkeyErrorText => string.Join(" ", new[] { ReplayHotkeyError, RecordHotkeyError }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
    public event Action<RecorderNotice>? Notice;
    public event Action? StateChanged;
    public event Action? LibraryChanged;

    public AppController() : this(SettingsStore.Load(), true)
    {
    }

    internal AppController(AppSettings settings, bool applyStartupRegistration, bool persistSettings = true)
    {
        _persistSettings = persistSettings;
        Settings = settings;
        Settings.Clamp();
        AppTheme.Apply(Settings.AccentColor);
        Capture = new CaptureSelection { Kind = CaptureKind.Monitor, MonitorDeviceName = Settings.MonitorDeviceName };
        Directory.CreateDirectory(Settings.OutputDirectory);
        _libraryRoot = Settings.OutputDirectory;
        if (applyStartupRegistration)
            StartupRegistration.Apply(Settings.StartWithWindows);
        _recorder.Notice += (_, notice) => Notice?.Invoke(notice);
        _watch = new DispatcherTimerShim(TimeSpan.FromSeconds(2), WatchGame);
        // Foreground changes arrive as out-of-context WinEvents on this (UI) thread's message loop:
        // no polling and no extra thread. The timer remains only as a slow fallback.
        _foregroundProc = (_, _, _, _, _, _, _) => WatchGame();
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _foregroundProc, 0, 0, WinEventOutOfContext);
        StateChanged += UpdateWatch;
        UpdateWatch();
    }

    public RecorderSnapshot Snapshot() => _recorder.GetSnapshot();

    public bool IsBusy => _recorder.IsBusy;

    public IReadOnlyList<string> Recover()
    {
        var recovered = _recorder.RecoverInterrupted(Settings.OutputDirectory);
        // Finish pending recovery journals before relocating their published files.
        if (_persistSettings && ClipLibrary.OrganizeLegacyFiles(Settings.OutputDirectory) > 0)
            LibraryChanged?.Invoke();
        return recovered;
    }

    public IReadOnlyList<DisplayEntry> Displays() => _recorder.ListDisplays();

    public IReadOnlyList<WindowEntry> Windows() => _recorder.ListWindows();

    public IReadOnlyList<AudioDeviceEntry> AudioDevices() => _recorder.ListAudioDevices();
    public IReadOnlyList<AudioApplicationEntry> AudioApplications() => _recorder.ListAudioApplications(Settings.AudioApplicationIds);
    public bool ApplicationAudioSupported => _recorder.ApplicationAudioSupported;

    public IReadOnlyList<EncoderDescription> Encoders()
    {
        if (_encoderCache == null || Environment.TickCount64 >= _encoderRefreshAt)
        {
            // Probing hardware encoders loads GPU driver DLLs that keep ~25 threads and ~50 MB alive
            // for the rest of the process. Probe in a short-lived child process while idle instead.
            _encoderCache = DetectEncodersOutOfProcess() ?? _recorder.DetectEncoders();
            _encoderRefreshAt = Environment.TickCount64 + 5 * 60_000;
        }
        return _encoderCache;
    }

    internal const string DetectEncodersArgument = "--detect-encoders";

    internal static IReadOnlyList<EncoderDescription>? DetectEncodersOutOfProcess()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null || !Path.GetFileName(exe).Equals("Spezi.exe", StringComparison.OrdinalIgnoreCase)) return null;
            using var process = Process.Start(new ProcessStartInfo(exe, DetectEncodersArgument)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            });
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(10_000)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
            if (process.ExitCode != 0) return null;
            return ParseEncoderLines(output.Result);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            AppLog.Error("Out-of-process encoder detection failed.", ex);
            return null;
        }
    }

    internal static string FormatEncoderLines(IEnumerable<EncoderDescription> encoders) =>
        string.Concat(encoders.Select(e => $"ENC\t{e.IsHardware}\t{e.VendorId}\t{e.Vendor.Replace('\t', ' ')}\t{e.Name.Replace('\t', ' ')}\n"));

    internal static IReadOnlyList<EncoderDescription> ParseEncoderLines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r').Split('\t')).Where(p => p.Length == 5 && p[0] == "ENC")
            .Select(p => new EncoderDescription { IsHardware = bool.Parse(p[1]), VendorId = uint.Parse(p[2]), Vendor = p[3], Name = p[4] }).ToList();

    public bool SoftwareAvailable() => _recorder.SoftwareEncoderAvailable();

    public void SelectMonitor(DisplayEntry display)
    {
        Capture = new CaptureSelection { Kind = CaptureKind.Monitor, MonitorDeviceName = display.DeviceName };
        Settings.MonitorDeviceName = display.DeviceName;
        Save();
    }

    public void SelectWindow(WindowEntry window)
    {
        Capture = new CaptureSelection
        {
            Kind = CaptureKind.Window,
            Handle = window.Handle,
            MonitorDeviceName = Settings.MonitorDeviceName
        };
    }

    public void Save()
    {
        Settings.Clamp();
        AppTheme.Apply(Settings.AccentColor);
        if (!string.Equals(_libraryRoot, Settings.OutputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _libraryRoot = Settings.OutputDirectory;
            if (_persistSettings) ClipLibrary.OrganizeLegacyFiles(_libraryRoot);
            LibraryChanged?.Invoke();
        }
        if (_persistSettings)
        {
            SettingsStore.Save(Settings);
            StartupRegistration.Apply(Settings.StartWithWindows);
        }
        if (_recorder.IsBusy)
        {
            _recorder.UpdateReplayDuration(Settings.ReplaySeconds, Settings.VideoBitrateKbps);
        }
        if (_recorder.IsBusy || _recorder.GetSnapshot().AudioTestActive) _recorder.UpdateGains(Settings);
        StateChanged?.Invoke();
    }

    internal void BindHotkeys(HotkeyHost host, bool register = true)
    {
        // Release both old assignments before registering either replacement.
        // This also permits swapping the two shortcuts without a false conflict.
        host.Unregister(1);
        host.Unregister(2);
        ReplayHotkeyError = "";
        RecordHotkeyError = "";
        if (Settings.ReplayHotkey.ConflictsWith(Settings.RecordHotkey))
        {
            var message = "Replay and recording use the same shortcut. Choose two different hotkeys.";
            ReplayHotkeyError = message;
            RecordHotkeyError = message;
            Notice?.Invoke(new RecorderNotice { Kind = NoticeKind.Error, Message = message });
            return;
        }

        if (register && !host.TryRegister(1, Settings.ReplayHotkey, out var replayError))
            ReplayHotkeyError = replayError;
        if (register && !host.TryRegister(2, Settings.RecordHotkey, out var recordError))
            RecordHotkeyError = recordError;

        var combined = HotkeyErrorText;
        if (!string.IsNullOrWhiteSpace(combined) && combined != _lastHotkeyError)
            Notice?.Invoke(new RecorderNotice { Kind = NoticeKind.Error, Message = combined });
        _lastHotkeyError = combined;
        StateChanged?.Invoke();
    }

    public void HandleHotkey(int id)
    {
        if (id == 1)
        {
            var snapshot = Snapshot();
            if (!snapshot.ReplayActive)
            {
                Notice?.Invoke(new RecorderNotice
                {
                    Kind = NoticeKind.Information,
                    Message = "Replay is off. Enable it in Spezi or turn on automatic replay when gaming."
                });
                return;
            }
            _ = SaveClip();
            return;
        }

        if (id == 2)
            ToggleSession();
    }

    public void StartReplay()
    {
        Run(() => _recorder.StartReplay(Request()));
        UpdateWatch();
    }

    public void StopReplay()
    {
        Run(_recorder.StopReplay);
    }

    public async Task SaveClip()
    {
        try
        {
            await _recorder.SaveClipAsync(GameTitles.Detect(Capture));
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    public void ToggleSession()
    {
        if (Snapshot().SessionActive)
        {
            _ = StopSession();
            return;
        }
        Run(() => _recorder.StartSession(Request()));
        UpdateWatch();
    }

    public async Task StopSession()
    {
        try
        {
            await _recorder.StopSessionAsync();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    public long FreeBytes()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(Settings.OutputDirectory));
            if (string.IsNullOrWhiteSpace(root))
                return 0;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    public void ApplyProfile(QualityProfile profile)
    {
        var display = Displays().FirstOrDefault(d => d.DeviceName.Equals(Settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                      ?? Displays().FirstOrDefault(d => d.IsPrimary)
                      ?? Displays().FirstOrDefault();
        AppSettings.ApplyProfile(Settings, profile, display?.Width ?? 1920, display?.Height ?? 1080);
        Save();
    }

    RecordingRequest Request(string? gameTitle = null) => new()
    {
        Settings = Settings,
        Capture = Capture,
        ReplayDuration = TimeSpan.FromSeconds(Settings.ReplaySeconds),
        GameTitle = gameTitle ?? GameTitles.Detect(Capture)
    };

    void WatchGame()
    {
        UpdateWatch();
        if (Settings.AutoReplayWhenPlaying || Snapshot().PipelineRunning)
            WatchGame(GameTitles.Detect(Capture));
    }

    // Poll only when something needs the game title: 2 s while capturing (title tracking),
    // 10 s while idle (the foreground hook handles auto-replay instantly).
    void UpdateWatch()
    {
        var running = Snapshot().PipelineRunning;
        if (running) _watch.Run(TimeSpan.FromSeconds(2));
        else if (Settings.AutoReplayWhenPlaying && _foregroundHook == IntPtr.Zero) _watch.Run(TimeSpan.FromSeconds(2));
        else _watch.Run(TimeSpan.FromSeconds(10));  // idle: cheap state check + foreground-hook fallback
    }

    delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    const uint EventSystemForeground = 0x0003, WinEventOutOfContext = 0x0000;
    readonly WinEventProc _foregroundProc;
    readonly IntPtr _foregroundHook;
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);

    internal void WatchGame(string? gameTitle)
    {
        var snapshot = Snapshot();
        if (snapshot.AudioTestActive) return;
        if (!snapshot.PipelineRunning && !Settings.AutoReplayWhenPlaying) return;
        if (snapshot.PipelineRunning)
            _recorder.UpdateGameTitle(gameTitle);
        // Automatic replay controls starting only. Losing foreground game focus
        // must never stop capture or erase the already compressed replay buffer.
        if (!Settings.AutoReplayWhenPlaying || gameTitle == null || snapshot.PipelineRunning)
            return;
        try
        {
            _recorder.StartReplay(Request(gameTitle));
            UpdateWatch();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    static bool ForegroundLooksLikeGame()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0)
            return false;
        GetWindowThreadProcessId(hwnd, out var processId);
        string name;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            name = process.ProcessName;
        }
        catch
        {
            return false;
        }

        if (IgnoredProcesses.Contains(name))
            return false;
        if (!GetWindowRect(hwnd, out var rect))
            return false;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 640 || height < 480)
            return false;

        var monitors = CaptureMonitorArea();
        foreach (var monitor in monitors)
        {
            var area = Math.Max(1, monitor.Width * monitor.Height);
            var overlapW = Math.Max(0, Math.Min(rect.Right, monitor.Right) - Math.Max(rect.Left, monitor.Left));
            var overlapH = Math.Max(0, Math.Min(rect.Bottom, monitor.Bottom) - Math.Max(rect.Top, monitor.Top));
            if (overlapW * overlapH >= area * 0.85)
                return true;
        }
        return false;
    }

    static List<MonitorRect> CaptureMonitorArea()
    {
        var list = new List<MonitorRect>();
        EnumDisplayMonitors(0, 0, Collect, 0);
        return list;

        bool Collect(nint monitor, nint hdc, ref WinRect rect, nint data)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                list.Add(new MonitorRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom));
            return true;
        }
    }

    void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    void Report(Exception ex)
    {
        Spezi.Core.AppLog.Error("User action failed.", ex);
        var message = ex is InvalidOperationException or IOException or UnauthorizedAccessException
            ? ex.Message
            : "That did not work. See the log for details.";
        if (message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) || ex is UnauthorizedAccessException)
            message = "Cannot write to the save folder. Choose another folder in Settings.";
        Notice?.Invoke(new RecorderNotice { Kind = NoticeKind.Error, Message = message });
    }

    public void NoteDiskSpace()
    {
        var free = FreeBytes();
        if (free <= 0)
            return;
        if (free < 8L * 1024 * 1024 * 1024)
        {
            if (_diskWarned)
                return;
            _diskWarned = true;
            Notice?.Invoke(new RecorderNotice
            {
                Kind = NoticeKind.DiskLow,
                Message = "Disk space is running low. Less than 8 GB is available."
            });
            return;
        }
        _diskWarned = false;
    }

    public void Dispose()
    {
        _watch.Stop();
        StateChanged -= UpdateWatch;
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        try
        {
            if (Snapshot().SessionActive)
                _recorder.StopSessionAsync().GetAwaiter().GetResult();
        }
        catch { /* shutting down */ }
        _recorder.Dispose();
    }

    static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Spezi", "explorer", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost",
        "ApplicationFrameHost", "TextInputHost", "SystemSettings", "LockApp", "Idle",
        "dwm", "sihost", "ctfmon", "chrome", "msedge", "firefox", "opera", "brave"
    };

    readonly record struct MonitorRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(nint hWnd, out WinRect rect);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorProc proc, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    delegate bool MonitorProc(nint monitor, nint hdc, ref WinRect rect, nint data);

    struct WinRect
    {
        public int Left, Top, Right, Bottom;
    }

    struct MonitorInfo
    {
        public int Size;
        public WinRect Monitor;
        public WinRect Work;
        public uint Flags;
    }
}

sealed class DispatcherTimerShim
{
    readonly System.Windows.Threading.DispatcherTimer _timer;

    public DispatcherTimerShim(TimeSpan interval, Action tick)
    {
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => tick();
        _timer.Start();
    }

    public void Run(TimeSpan interval)
    {
        if (_timer.Interval != interval) _timer.Interval = interval;
        if (!_timer.IsEnabled) _timer.Start();
    }

    public void Stop() => _timer.Stop();
}
