using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Spezi.Core.Recording;

namespace Spezi.Core;

public sealed class Recorder : IDisposable
{
    readonly object _gate = new();
    readonly ReplayBuffer _replay = new();
    readonly AudioEngine _audio = new();
    readonly string _sessionRoot;
    GraphicsDevice? _graphics;
    ColorConverter? _converter;
    H264Encoder? _videoEncoder;
    AacEncoder? _audioEncoder;
    FramePump? _pump;
    SegmentedSession? _session;
    Task<string?>? _sessionStopTask;
    MediaClock? _clock;
    RecordingRequest? _request;
    bool _replayEnabled;
    bool _sessionEnabled;
    bool _audioTest;
    bool _pipeline;
    long _sessionStart;
    long _encodedFrames;
    long _captureWindowStart;
    int _captureWindowCount;
    int _encodedWindowCount;
    double _captureFps;
    double _encodedFps;
    bool _diskWarned;
    string? _lostAudioName;
    string? _replayGameTitle;
    long _gameLastSeen;

    public event EventHandler<RecorderNotice>? Notice;

    public Recorder() : this(null) { }

    internal Recorder(string? sessionRoot)
    {
        _sessionRoot = sessionRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spezi", "sessions");
        MfBootstrap.Ensure();
        _audio.PcmReady += OnPcm;
        _audio.DeviceLost += OnDeviceLost;
        _audio.DefaultDeviceChanged += OnDefaultDeviceChanged;
        _audio.ApplicationUnavailable += name => Raise(NoticeKind.AudioDeviceLost, $"Audio from “{name}” could not be started. Spezi will retry.");
    }

    public IReadOnlyList<EncoderDescription> DetectEncoders()
    {
        var displays = CaptureSources.ListDisplays();
        uint vendor = 0;
        if (displays.Count > 0)
        {
            using var device = GraphicsDevice.CreateForMonitor(displays[0].MonitorHandle);
            vendor = device.VendorId;
        }
        return EncoderCatalog.Detect(vendor);
    }

    public bool SoftwareEncoderAvailable() => EncoderCatalog.HasSoftwareEncoder();

    public IReadOnlyList<AudioDeviceEntry> ListAudioDevices() => AudioEngine.ListDevices();
    public IReadOnlyList<AudioApplicationEntry> ListAudioApplications(IEnumerable<string>? selected = null) => ApplicationAudioCatalog.ListApplications(selected);
    public bool ApplicationAudioSupported => ProcessLoopbackCapture.IsSupported;
    public IReadOnlyList<DisplayEntry> ListDisplays() => CaptureSources.ListDisplays();
    public IReadOnlyList<WindowEntry> ListWindows() => CaptureSources.ListWindows();

    public void StartReplay(RecordingRequest request)
    {
        if (GetSnapshot().AudioTestActive) StopAudioTest();
        lock (_gate)
        {
            EnsureCompatible(request);
            _request = request;
            _replay.Configure(request.ReplayDuration, request.Settings.VideoBitrateKbps);
            if (!_pipeline)
                StartPipeline(request);
            _replayGameTitle = request.GameTitle;
            _gameLastSeen = _clock?.Now() ?? 0;
            _replayEnabled = true;
        }
        Raise(NoticeKind.ReplayStarted, $"Replay is on. Clip length: {AppSettings.ReplayDurationText(request.Settings.ReplaySeconds)}");
    }

    public void StopReplay()
    {
        var stopPipeline = false;
        lock (_gate)
        {
            _replayEnabled = false;
            _replay.Clear();
            stopPipeline = _pipeline && !_sessionEnabled && !_audioTest;
        }
        if (stopPipeline)
            StopPipeline();
        Raise(NoticeKind.ReplayStopped, "Replay stopped.");
    }

    public Task<string?> SaveClipAsync(string? gameTitle = null)
    {
        EncodedPacket[] video;
        EncodedPacket[] audio;
        TimeSpan buffered;
        IMFMediaType? videoType;
        IMFMediaType? audioType;
        string directory;
        lock (_gate)
        {
            if (!_replayEnabled)
                throw new InvalidOperationException("Replay is not active.");
            (video, audio, buffered) = _replay.Snapshot();
            videoType = CloneType(_videoEncoder?.OutputType);
            audioType = CloneType(_audioEncoder?.OutputType);
            directory = _request?.Settings.OutputDirectory ?? SettingsStore.Load().OutputDirectory;
            gameTitle ??= video.Length > 0 && _gameLastSeen >= video[0].Timestamp ? _replayGameTitle : null;
        }

        if (video.Length == 0 || videoType == null)
        {
            videoType?.Dispose();
            audioType?.Dispose();
            throw new InvalidOperationException("The replay buffer is still empty. Wait a moment and try again.");
        }

        return Task.Run(() =>
        {
            using (videoType)
            using (audioType)
            {
                directory = ClipLibrary.GameDirectory(directory, gameTitle);
                EnsureWritable(directory);
                var path = Path.Combine(directory, RecordingNames.Create("Clip", gameTitle));
                var pendingPath = PendingPath(path);
                WritePackets(pendingPath, videoType, audioType, video, audio);
                File.Move(pendingPath, path);
                Raise(NoticeKind.ClipSaved, $"Clip saved ({FormatDuration(buffered)}).", path);
                return (string?)path;
            }
        });
    }

    public void StartSession(RecordingRequest request)
    {
        if (GetSnapshot().AudioTestActive) StopAudioTest();
        lock (_gate)
        {
            EnsureCompatible(request);
            _request = request;
            if (!_pipeline)
                StartPipeline(request);
            if (_sessionEnabled)
                return;
            var folder = Path.Combine(_sessionRoot,
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N"));
            var recentGame = _replayEnabled && (_clock?.Now() ?? 0) - _gameLastSeen <= request.ReplayDuration.Ticks ? _replayGameTitle : null;
            _session = new SegmentedSession(folder, request.GameTitle ?? recentGame);
            if (_videoEncoder?.OutputType != null)
                _session.SetTypes(_videoEncoder.OutputType, _audioEncoder?.OutputType);
            _sessionEnabled = true;
            _sessionStart = _clock?.Now() ?? 0;
            _videoEncoder?.RequestKeyframe();
            _diskWarned = false;
        }
        Raise(NoticeKind.SessionStarted, "Recording started.");
    }

    public Task<string?> StopSessionAsync()
    {
        lock (_gate)
        {
            if (_sessionStopTask is { IsCompleted: false })
                return _sessionStopTask;
            _sessionStopTask = Task.Run(StopSession);
            return _sessionStopTask;
        }
    }

    string? StopSession()
    {
        bool stopPipeline;
        lock (_gate)
            stopPipeline = _sessionEnabled && _pipeline && !_replayEnabled && !_audioTest;
        // Keep the session attached until both encoders have delivered queued samples.
        if (stopPipeline)
            StopPipeline();
        SegmentedSession? session;
        string directory;
        lock (_gate)
        {
            if (!_sessionEnabled || _session == null)
                return null;
            session = _session;
            _session = null;
            _sessionEnabled = false;
            directory = _request?.Settings.OutputDirectory ?? SettingsStore.Load().OutputDirectory;
        }

            string? path = null;
            try
            {
                var parts = session.Finish();
                if (parts.Count == 0)
                    throw new InvalidOperationException("The recording has no video frames yet. Wait briefly after starting and check the capture source.");
                directory = ClipLibrary.GameDirectory(directory, session.GameTitle);
                EnsureWritable(directory);
                var destination = Path.Combine(directory, RecordingNames.Create("Recording", session.GameTitle));
                var pendingPath = PendingPath(destination);
                SegmentedSession.Remux(parts, pendingPath);
                File.Move(pendingPath, destination);
                path = destination;
                try { System.IO.Directory.Delete(session.DirectoryPath, true); } catch (Exception ex) { AppLog.Error("Session folder could not be removed; parts are kept.", ex); }
            }
            catch (Exception ex)
            {
                AppLog.Error("Session finalize failed.", ex);
                Raise(NoticeKind.Error, "The recording could not be fully saved. Existing parts are in the local session folder. " + ex.Message);
            }
            finally
            {
                session.Dispose();
            }

            if (path != null)
                Raise(NoticeKind.SessionStopped, "Recording saved.", path);
            return path;
    }

    public void StartAudioTest(AppSettings settings)
    {
        lock (_gate)
        {
            if (_pipeline)
                throw new InvalidOperationException("Audio testing is not needed while recording. The level meters are already active.");
            _clock = new MediaClock();
            _audio.Start(settings, _clock);
            _audioTest = true;
            _pipeline = true;
        }
    }

    public void StopAudioTest()
    {
        var stop = false;
        lock (_gate)
        {
            _audioTest = false;
            stop = _pipeline && !_replayEnabled && !_sessionEnabled;
        }
        if (stop)
            StopPipeline();
    }

    public void UpdateReplayDuration(int seconds, int bitrateKbps)
    {
        lock (_gate)
        {
            var duration = TimeSpan.FromSeconds(AppSettings.ValidReplaySeconds(seconds));
            if (_request != null) _request.ReplayDuration = duration;
            _replay.Configure(duration, bitrateKbps);
        }
    }

    public void UpdateGains(AppSettings settings) => _audio.UpdateGains(settings);

    public void UpdateGameTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        lock (_gate)
        {
            _replayGameTitle = title;
            _gameLastSeen = _clock?.Now() ?? 0;
            _session?.SetGameTitle(title);
        }
    }

    public IReadOnlyList<string> RecoverInterrupted(string outputDirectory)
    {
        var recovered = SessionRecovery.Recover(_sessionRoot, outputDirectory);
        if (recovered.Count > 0)
            Raise(NoticeKind.RecoveredRecording, recovered.Count == 1
                ? "An interrupted recording was recovered."
                : $"{recovered.Count} interrupted recordings were recovered.");
        return recovered;
    }

    public RecorderSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var elapsed = _sessionEnabled && _clock != null
                ? TimeSpan.FromTicks(Math.Max(0, _clock.Now() - _sessionStart))
                : TimeSpan.Zero;
            return new RecorderSnapshot
            {
                PipelineRunning = _pipeline && !_audioTest,
                ReplayActive = _replayEnabled,
                SessionActive = _sessionEnabled,
                AudioTestActive = _audioTest,
                UsingSoftwareEncoder = _videoEncoder is { IsHardware: false },
                EncoderName = _videoEncoder?.Name ?? "",
                SessionElapsed = elapsed,
                ReplayBuffered = _replayEnabled ? _replay.Buffered : TimeSpan.Zero,
                ReplaySeconds = _request?.Settings.ReplaySeconds ?? 0,
                CaptureFps = _captureFps,
                EncodedFps = _encodedFps,
                DroppedFrames = _videoEncoder?.DroppedFrames ?? 0,
                ReplayBytes = _replay.Bytes,
                Levels = _audio.Levels
            };
        }
    }

    public bool IsBusy
    {
        get { lock (_gate) return _pipeline && !_audioTest; }
    }

    void StartPipeline(RecordingRequest request)
    {
        EnsureWritable(request.Settings.OutputDirectory);
        EnsureDisk(request.Settings.OutputDirectory, starting: true);
        var hardware = EncoderCatalog.Detect(0);
        if (hardware.Count == 0 && !request.Settings.AllowSoftwareEncoder)
            throw new InvalidOperationException("No hardware encoder found. Enable software encoding only if you accept the additional CPU usage.");

        DisplayEntry display;
        nint window = 0;
        if (request.Capture.Kind == CaptureKind.Window)
        {
            window = request.Capture.Handle;
            if (window == 0)
                throw new InvalidOperationException("The selected window is no longer available.");
            display = CaptureSources.ResolveMonitor(request.Settings.MonitorDeviceName);
        }
        else
        {
            display = CaptureSources.ResolveMonitor(request.Capture.MonitorDeviceName);
        }

        _graphics = GraphicsDevice.CreateForMonitor(display.MonitorHandle);
        _clock = new MediaClock();
        _converter = new ColorConverter(_graphics.Device, _graphics.Context);
        _videoEncoder = new H264Encoder();
        _videoEncoder.Packet += OnVideoPacket;
        _audioEncoder = new AacEncoder();
        _audioEncoder.Packet += OnAudioPacket;
        _audioEncoder.Start();
        _videoEncoder.Start(
            _graphics.Device,
            request.Settings.Width,
            request.Settings.Height,
            request.Settings.FrameRate,
            request.Settings.VideoBitrateKbps,
            request.Settings.AllowSoftwareEncoder,
            _graphics.VendorId);
        _session?.SetTypes(_videoEncoder.OutputType!, _audioEncoder.OutputType);
        var isolate = Environment.GetEnvironmentVariable("SPEZI_ISOLATE") ?? "all";
        AppLog.Info("Isolate=" + isolate);
        if (isolate is "all" or "audio")
            _audio.Start(request.Settings, _clock, request.Capture);
        if (isolate is "all" or "video")
        {
            _pump = new FramePump(_graphics, _converter, _videoEncoder, _clock, request, OnSourceClosed, () => Interlocked.Increment(ref _captureWindowCount));
            _pump.Start(window, display.MonitorHandle);
        }
        _pipeline = true;
        _encodedFrames = 0;
        _captureWindowStart = Environment.TickCount64;
        AppLog.Info("Pipeline started. Encoder=" + _videoEncoder.Name);
    }

    void StopPipeline()
    {
        FramePump? pump;
        H264Encoder? video;
        AacEncoder? audioEncoder;
        ColorConverter? converter;
        GraphicsDevice? graphics;
        lock (_gate)
        {
            pump = _pump;
            video = _videoEncoder;
            audioEncoder = _audioEncoder;
            converter = _converter;
            graphics = _graphics;
            _pump = null;
            _videoEncoder = null;
            _audioEncoder = null;
            _converter = null;
            _graphics = null;
            _pipeline = false;
            _audioTest = false;
        }

        try { pump?.Dispose(); } catch (Exception ex) { AppLog.Error("Capture stop failed.", ex); }
        try { _audio.Stop(); } catch (Exception ex) { AppLog.Error("Audio stop failed.", ex); }
        try { video?.DrainAndStop(); } catch (Exception ex) { AppLog.Error("Encoder drain failed.", ex); }
        try { audioEncoder?.DrainAndStop(); } catch (Exception ex) { AppLog.Error("Audio encoder drain failed.", ex); }
        try { audioEncoder?.Dispose(); } catch (Exception ex) { AppLog.Error("Audio encoder dispose failed.", ex); }
        try { converter?.Dispose(); } catch (Exception ex) { AppLog.Error("Color converter dispose failed.", ex); }
        try { graphics?.Dispose(); } catch (Exception ex) { AppLog.Error("Graphics dispose failed.", ex); }
        video?.Dispose();
    }

    void OnVideoPacket(EncodedPacket packet)
    {
        Interlocked.Increment(ref _encodedFrames);
        var now = Environment.TickCount64;
        if (now - _captureWindowStart >= 1000)
        {
            var seconds = Math.Max(0.001, (now - _captureWindowStart) / 1000.0);
            _encodedFps = _encodedWindowCount / seconds;
            _captureFps = _captureWindowCount / seconds;
            _encodedWindowCount = 0;
            _captureWindowCount = 0;
            _captureWindowStart = now;
        }
        _encodedWindowCount++;

        if (_replayEnabled)
            _replay.Add(packet);
        SegmentedSession? session;
        string? directory;
        lock (_gate)
        {
            session = _sessionEnabled ? _session : null;
            directory = _request?.Settings.OutputDirectory;
        }
        if (session != null)
        {
            try
            {
                if (packet.IsKeyframe && _videoEncoder?.OutputType is { } currentVideoType)
                    session.SetTypes(currentVideoType, _audioEncoder?.OutputType);
                session.Write(packet);
                if (directory != null && packet.IsKeyframe)
                    EnsureDisk(directory, starting: false);
            }
            catch (Exception ex)
            {
                AppLog.Error("Writing the session failed.", ex);
                Raise(NoticeKind.Error, "The recording could not continue saving: " + ex.Message);
                _ = Task.Run(StopSessionAsync);
            }
        }
    }

    void OnAudioPacket(EncodedPacket packet)
    {
        if (_replayEnabled)
            _replay.Add(packet);
        SegmentedSession? session;
        lock (_gate)
            session = _sessionEnabled ? _session : null;
        try
        {
            session?.Write(packet);
        }
        catch (Exception ex)
        {
            AppLog.Error("Writing session audio failed.", ex);
            Raise(NoticeKind.Error, "The recording audio could not continue saving: " + ex.Message);
            _ = Task.Run(StopSessionAsync);
        }
    }

    void OnPcm(byte[] pcm, long timestamp)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("SPEZI_SKIP_ENCODE") == "1")
                return;
            _audioEncoder?.WritePcm16(pcm, timestamp);
        }
        catch (Exception ex)
        {
            AppLog.Error("Audio encode failed.", ex);
        }
    }

    void OnDeviceLost(string deviceId)
    {
        var name = _audio.Levels.FirstOrDefault(l => l.DeviceId == deviceId)?.Name ?? "Audio device";
        if (_lostAudioName == name)
            return;
        _lostAudioName = name;
        _audio.RemoveDevice(deviceId);
        Raise(NoticeKind.AudioDeviceLost, $"The audio device “{name}” is no longer available. Recording will continue without it.");
    }

    void OnDefaultDeviceChanged(string deviceId)
    {
        Raise(NoticeKind.AudioDeviceLost, "The default audio device changed. The current recording will keep its selected devices.");
    }

    void OnSourceClosed()
    {
        Raise(NoticeKind.CaptureSourceLost, "The capture source is no longer available. The window was closed or the display was disconnected.");
        if (_sessionEnabled)
            _ = StopSessionAsync();
        else if (_replayEnabled)
            StopReplay();
    }

    void EnsureCompatible(RecordingRequest request)
    {
        if (request.Settings.CaptureSystemAudio != false && request.Settings.AudioMode == AudioCaptureMode.Applications && !ProcessLoopbackCapture.IsSupported)
            throw new NotSupportedException("App audio requires Windows 11 or Windows build 20348 or later.");
        if (_sessionStopTask is { IsCompleted: false })
            throw new InvalidOperationException("The previous recording is still being saved. Wait a moment.");
        if (!_pipeline || _request == null || _audioTest)
            return;
        var same = _request.Capture.Kind == request.Capture.Kind &&
                   _request.Capture.Handle == request.Capture.Handle &&
                   string.Equals(_request.Capture.MonitorDeviceName, request.Capture.MonitorDeviceName, StringComparison.OrdinalIgnoreCase) &&
                   _request.Settings.Width == request.Settings.Width &&
                   _request.Settings.Height == request.Settings.Height &&
                   _request.Settings.FrameRate == request.Settings.FrameRate;
        if (!same)
            throw new InvalidOperationException("A recording with a different source or quality is already running. Stop it first.");
    }

    void EnsureWritable(string directory)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".spezi-write-test-" + Guid.NewGuid().ToString("N"));
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0);
        }
        catch (Exception ex)
        {
            AppLog.Error("Output folder is not writable.", ex);
            throw new InvalidOperationException("Cannot write to the save folder. Choose another folder in Settings.");
        }
    }

    void EnsureDisk(string directory, bool starting)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory));
        if (string.IsNullOrWhiteSpace(root))
            return;
        var free = new DriveInfo(root).AvailableFreeSpace;
        if (free < 1024L * 1024 * 1024)
        {
            if (starting)
                throw new InvalidOperationException("There is not enough disk space to record. At least 1 GB of free space is required.");
            Raise(NoticeKind.DiskFull, "The disk is full. The recording was saved and stopped.");
            _ = StopSessionAsync();
            return;
        }

        if (free < 8L * 1024 * 1024 * 1024 && !_diskWarned)
        {
            _diskWarned = true;
            Raise(NoticeKind.DiskLow, "Disk space is running low. Less than 8 GB is available.");
        }
    }

    static void WritePackets(string path, IMFMediaType videoType, IMFMediaType? audioType, EncodedPacket[] video, EncodedPacket[] audio)
    {
        using var attributes = SegmentedSession.CreateWriterAttributes();
        var writer = MediaFactory.MFCreateSinkWriterFromURL(path, null!, attributes);
        try
        {
            var videoStream = writer.AddStream(videoType);
            writer.SetInputMediaType(videoStream, videoType, null!);
            var audioStream = -1;
            if (audioType != null && audio.Length > 0)
            {
                audioStream = writer.AddStream(audioType);
                writer.SetInputMediaType(audioStream, audioType, null!);
            }
            writer.BeginWriting();
            var origin = video[0].Timestamp;
            var merged = new List<(EncodedPacket Packet, int Stream)>(video.Length + audio.Length);
            foreach (var packet in video)
                merged.Add((packet, videoStream));
            if (audioStream >= 0)
            {
                foreach (var packet in audio)
                    merged.Add((packet, audioStream));
            }
            merged.Sort((a, b) => a.Packet.Timestamp.CompareTo(b.Packet.Timestamp));
            var last = new long[8];
            for (var i = 0; i < last.Length; i++)
                last[i] = -1;
            foreach (var item in merged)
            {
                var time = item.Packet.Timestamp - origin;
                if (time < 0 || item.Stream >= last.Length || time <= last[item.Stream])
                    continue;
                last[item.Stream] = time;
                WriteOne(writer, item.Stream, item.Packet, time);
            }
            writer.Finalize();
        }
        finally
        {
            writer.Dispose();
        }
    }

    static IMFMediaType? CloneType(IMFMediaType? source)
    {
        if (source == null)
            return null;
        var clone = MediaFactory.MFCreateMediaType();
        source.CopyAllItems(clone);
        return clone;
    }

    static string PendingPath(string path)
    {
        var directory = Path.Combine(Path.GetDirectoryName(path)!, ".spezi-pending");
        System.IO.Directory.CreateDirectory(directory);
        return Path.Combine(directory, Path.GetFileName(path));
    }

    static void WriteOne(IMFSinkWriter writer, int stream, EncodedPacket packet, long timestamp)
    {
        var buffer = MediaFactory.MFCreateMemoryBuffer(packet.Data.Length);
        H264Encoder.WriteBuffer(buffer, packet.Data);
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        buffer.Dispose();
        sample.SampleTime = timestamp;
        sample.SampleDuration = Math.Max(1, packet.Duration);
        if (packet.IsKeyframe)
            sample.Set(SampleAttributeKeys.CleanPoint, 1u);
        writer.WriteSample(stream, sample);
        sample.Dispose();
    }

    void Raise(NoticeKind kind, string message, string? path = null) =>
        Notice?.Invoke(this, new RecorderNotice { Kind = kind, Message = message, FilePath = path });

    static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return duration.ToString(@"h\:mm\:ss");
        return duration.ToString(@"m\:ss");
    }

    public void Dispose()
    {
        try
        {
            if (_sessionEnabled)
                StopSessionAsync().GetAwaiter().GetResult();
        }
        catch { /* shutting down */ }
        StopPipeline();
        _audio.Dispose();
    }
}

public sealed class RecordingRequest
{
    public required AppSettings Settings { get; init; }
    public required CaptureSelection Capture { get; init; }
    public TimeSpan ReplayDuration { get; set; }
    public string? GameTitle { get; init; }
}

sealed class FramePump : IDisposable
{
    readonly GraphicsDevice _graphics;
    readonly ColorConverter _converter;
    readonly H264Encoder _encoder;
    readonly MediaClock _clock;
    readonly RecordingRequest _request;
    readonly Action _closed;
    readonly Action _captured;
    GraphicsCaptureItem? _item;
    Direct3D11CaptureFramePool? _pool;
    GraphicsCaptureSession? _session;
    readonly AutoResetEvent _wake = new(false);
    Thread? _thread;
    volatile bool _running;
    ID3D11Texture2D? _cachedNv12;
    byte[]? _cachedBytes;
    bool _closedRaised;
    int _conversionErrors;
    int _loggedFrame;
    static int SurfaceQueryFailures;

    public FramePump(GraphicsDevice graphics, ColorConverter converter, H264Encoder encoder, MediaClock clock, RecordingRequest request, Action closed, Action captured)
    {
        _graphics = graphics;
        _converter = converter;
        _encoder = encoder;
        _clock = clock;
        _request = request;
        _closed = closed;
        _captured = captured;
    }

    public void Start(nint window, nint monitor)
    {
        _item = window != 0
            ? Native.CreateCaptureItemForWindow(window)
            : Native.CreateCaptureItemForMonitor(monitor);
        _item.Closed += (_, _) =>
        {
            if (_closedRaised)
                return;
            _closedRaised = true;
            ThreadPool.QueueUserWorkItem(_ => _closed());
        };

        var size = _item.Size;
        _converter.Ensure(size.Width, size.Height, _request.Settings.Width, _request.Settings.Height);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _graphics.WinRtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            size);
        _pool.FrameArrived += OnFrame;
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = _request.Settings.CaptureCursor;
        Native.TryDisableCaptureBorder(_session);
        _session.StartCapture();
        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "Spezi Capture" };
        _thread.Start();
    }

    void OnFrame(Direct3D11CaptureFramePool sender, object args)
    {
        _wake.Set();
    }

    void CaptureLoop()
    {
        using var timer = new CaptureTimer(_wake);
        var duration = 10_000_000L / _request.Settings.FrameRate;
        var next = _clock.Now();
        while (_running)
        {
            var now = _clock.Now();
            if (now < next)
            {
                timer.WaitFor(next - now);
                continue;
            }
            try
            {
                Direct3D11CaptureFrame? newest = null;
                try
                {
                    // Windows reports changed frames; reuse the last image on quiet scenes.
                    for (var i = 0; i < 4; i++)
                    {
                        var frame = _pool?.TryGetNextFrame();
                        if (frame == null)
                            break;
                        newest?.Dispose();
                        newest = frame;
                    }
                    if (newest != null)
                    {
                        var content = newest.ContentSize;
                        if (content.Width >= 2 && content.Height >= 2)
                            _converter.Ensure(content.Width, content.Height, _request.Settings.Width, _request.Settings.Height);
                        using var texture = TextureFromSurface(newest.Surface);
                        if (texture != null)
                        {
                            if (_loggedFrame++ == 0)
                            {
                                var description = texture.Description;
                                AppLog.Info($"Capture texture {description.Width}x{description.Height} format={description.Format} bind={description.BindFlags} array={description.ArraySize}");
                            }
                            var converted = _converter.Convert(texture);
                            _cachedNv12?.Dispose();
                            _cachedNv12 = converted;
                            if (!_encoder.IsHardware)
                                _cachedBytes = _converter.ReadNv12(converted);
                        }
                    }
                }
                finally { newest?.Dispose(); }

                var timestamp = _clock.Now();
                if (_cachedNv12 != null)
                {
                    if (_encoder.IsHardware)
                        _encoder.SubmitTexture(_cachedNv12.QueryInterface<ID3D11Texture2D>(), timestamp, false);
                    else if (_cachedBytes != null)
                        _encoder.SubmitNv12(_cachedBytes, timestamp, false);
                    _captured();
                }
            }
            catch (Exception ex)
            {
                if (Interlocked.Increment(ref _conversionErrors) <= 3)
                    AppLog.Error("Frame conversion failed.", ex);
            }
            next += duration;
            // Preserve real time when capture is late instead of submitting a catch-up burst.
            if (_clock.Now() >= next + duration)
                next = _clock.Now() + duration;
        }
    }

    static ID3D11Texture2D? TextureFromSurface(Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface)
    {
        nint unknown = 0;
        var releaseUnknown = false;
        if (surface is WinRT.IWinRTObject winrtObject)
            unknown = winrtObject.NativeObject.ThisPtr;
        else
        {
            unknown = Marshal.GetIUnknownForObject(surface);
            releaseUnknown = true;
        }

        try
        {
            var accessId = typeof(Native.IDirect3DDxgiInterfaceAccess).GUID;
            var hr = Marshal.QueryInterface(unknown, in accessId, out var accessPointer);
            if (hr < 0)
            {
                if (Interlocked.Increment(ref SurfaceQueryFailures) == 1)
                    AppLog.Error($"Surface QueryInterface failed: 0x{hr:X8}");
                return null;
            }
            try
            {
                var access = (Native.IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(accessPointer);
                var textureId = Native.Texture2DIid;
                hr = access.GetInterface(ref textureId, out var texturePointer);
                if (hr < 0)
                    return null;
                return new ID3D11Texture2D(texturePointer);
            }
            finally
            {
                Marshal.Release(accessPointer);
            }
        }
        finally
        {
            if (releaseUnknown)
                Marshal.Release(unknown);
        }
    }

    public void Dispose()
    {
        _running = false;
        _wake.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        if (_session != null)
        {
            try { _session.Dispose(); } catch (Exception ex) { AppLog.Error("Session dispose failed.", ex); }
            _session = null;
        }
        if (_pool != null)
        {
            _pool.FrameArrived -= OnFrame;
            _pool.Dispose();
            _pool = null;
        }
        _item = null;
        _cachedNv12?.Dispose();
        _cachedNv12 = null;
        _cachedBytes = null;
        _wake.Dispose();
    }
}
