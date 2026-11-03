using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Spezi.Core.Recording;

internal sealed class AudioEngine : IDisposable
{
    const int Rate = AacEncoder.SampleRate;
    static readonly Guid IeeeFloatSubformat = new("00000003-0000-0010-8000-00aa00389b71");
    readonly object _gate = new();
    readonly AutoResetEvent _mixWake = new(false);
    readonly List<CaptureSlot> _slots = [];
    CaptureSlot[] _activeSlots = [];
    readonly MMDeviceEnumerator _enumerator = new();
    readonly DeviceNotifications _notifications;
    Thread? _mixThread;
    Thread? _applicationThread;
    CancellationTokenSource? _applicationStop;
    ConcurrentDictionary<string, double>? _applicationGains;
    volatile bool _applicationMode;
    volatile bool _running;
    long _mixedFrames;
    MediaClock? _clock;
    bool _silenceOnly;

    public event Action<byte[], long>? PcmReady;
    public event Action<string>? DeviceLost;
    public event Action<string>? DefaultDeviceChanged;
    public event Action<string>? ApplicationUnavailable;
    public IReadOnlyList<AudioLevel> Levels { get; private set; } = [];

    public AudioEngine()
    {
        _notifications = new DeviceNotifications(
            id => { if (Volatile.Read(ref _activeSlots).Any(slot => slot.Id == id)) DeviceLost?.Invoke(id); },
            id => { if (_running && !_applicationMode) DefaultDeviceChanged?.Invoke(id); });
        _enumerator.RegisterEndpointNotificationCallback(_notifications);
    }

    public static IReadOnlyList<AudioDeviceEntry> ListDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var list = new List<AudioDeviceEntry>();
        string? defaultRender = null;
        string? defaultCapture = null;
        try { defaultRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; } catch { /* no device */ }
        try { defaultCapture = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia).ID; } catch { /* no device */ }

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            list.Add(new AudioDeviceEntry
            {
                Id = device.ID,
                Name = device.FriendlyName,
                IsMicrophone = false,
                IsDefault = device.ID == defaultRender
            });
            device.Dispose();
        }

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            list.Add(new AudioDeviceEntry
            {
                Id = device.ID,
                Name = device.FriendlyName,
                IsMicrophone = true,
                IsDefault = device.ID == defaultCapture
            });
            device.Dispose();
        }

        return list;
    }

    public void Start(AppSettings settings, MediaClock clock, CaptureSelection? captureSelection = null)
    {
        Stop();
        if (settings.CaptureSystemAudio != false && settings.AudioMode == AudioCaptureMode.Applications && !ProcessLoopbackCapture.IsSupported)
            throw new NotSupportedException("App audio requires Windows 11 or Windows build 20348 or later. Use all system audio on this PC.");
        _clock = clock;
        _applicationMode = settings.AudioMode == AudioCaptureMode.Applications;
        _silenceOnly = Environment.GetEnvironmentVariable("SPEZI_SILENCE") == "1";
        if (_silenceOnly)
        {
            _running = true;
            _mixedFrames = 0;
            _mixThread = new Thread(MixLoop) { IsBackground = true, Name = "Spezi Audio" };
            _mixThread.Start();
            return;
        }
        var devices = ListDevices();
        if (settings.CaptureSystemAudio != false && settings.AudioMode == AudioCaptureMode.System)
        {
            var outputs = settings.OutputDeviceIds.Count == 0
                ? devices.Where(d => !d.IsMicrophone && d.IsDefault).ToList()
                : devices.Where(d => !d.IsMicrophone && settings.OutputDeviceIds.Contains(d.Id)).ToList();
            if (outputs.Count == 0)
                outputs = devices.Where(d => !d.IsMicrophone && d.IsDefault).ToList();

            foreach (var output in outputs.GroupBy(d => d.Id).Select(g => g.First()))
                AddSlot(output, false, settings.GainFor(output.Id));
        }

        if (settings.RecordMicrophone)
        {
            var mic = devices.FirstOrDefault(d => d.IsMicrophone && d.Id == settings.MicrophoneId)
                      ?? devices.FirstOrDefault(d => d.IsMicrophone && d.IsDefault);
            if (mic != null)
                AddSlot(mic, true, settings.GainFor(mic.Id));
        }

        foreach (var slot in _slots) slot.Mono = settings.MicrophoneMono;
        _running = true;
        _mixedFrames = 0;
        _mixThread = new Thread(MixLoop) { IsBackground = true, Name = "Spezi Audio" };
        _mixThread.Start();
        if (settings.CaptureSystemAudio != false && settings.AudioMode == AudioCaptureMode.Applications)
        {
            var selection = settings.AudioApplicationIds.ToArray();
            var gains = new ConcurrentDictionary<string, double>(selection.ToDictionary(id => id, settings.GainFor, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            _applicationGains = gains;
            _applicationStop = new CancellationTokenSource();
            var cancellation = _applicationStop.Token;
            // Discovery and activation stay off the UI and mix threads. New app processes
            // are attached automatically; they never trigger whole-device capture.
            _applicationThread = new Thread(() => WatchApplications(selection, gains, clock, captureSelection, cancellation))
                { IsBackground = true, Name = "Spezi Audio Apps" };
            _applicationThread.SetApartmentState(ApartmentState.MTA);
            _applicationThread.Start();
        }
    }

    public void UpdateGains(AppSettings settings)
    {
        lock (_gate)
        {
            if (_applicationGains != null)
                foreach (var id in _applicationGains.Keys) _applicationGains[id] = settings.GainFor(id);
            foreach (var slot in _slots)
            {
                slot.Gain = settings.GainFor(slot.GainId);
                slot.Mono = settings.MicrophoneMono;
            }
        }
    }

    public void RemoveDevice(string deviceId)
    {
        CaptureSlot? removed = null;
        lock (_gate)
        {
            removed = _slots.FirstOrDefault(s => s.Id == deviceId);
            if (removed != null)
            {
                _slots.Remove(removed);
                Volatile.Write(ref _activeSlots, _slots.ToArray());
            }
        }
        if (removed != null)
            ThreadPool.QueueUserWorkItem(_ => removed.Dispose());
    }

    void AddSlot(AudioDeviceEntry entry, bool microphone, double gain)
    {
        MMDevice? device = null;
        IWaveIn? capture = null;
        try
        {
            device = _enumerator.GetDevice(entry.Id);
            capture = microphone ? new WasapiCapture(device) : new WasapiLoopbackCapture(device);
            var slot = new CaptureSlot(entry.Id, entry.Name, microphone, gain, capture.WaveFormat);
            capture.DataAvailable += (_, args) =>
            {
                var stamp = _clock?.Now() ?? 0;
                var duration = args.BytesRecorded * 10_000_000L / Math.Max(1, slot.SourceFormat.AverageBytesPerSecond);
                slot.Append(args.Buffer, args.BytesRecorded, stamp - duration);
            };
            capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception != null)
                {
                    AppLog.Error("Audio device stopped: " + entry.Name, args.Exception);
                    DeviceLost?.Invoke(entry.Id);
                }
            };
            capture.StartRecording();
            slot.Capture = capture;
            slot.Device = device;
            device = null;
            lock (_gate)
            {
                _slots.Add(slot);
                Volatile.Write(ref _activeSlots, _slots.ToArray());
            }
        }
        catch (Exception ex)
        {
            capture?.Dispose();
            device?.Dispose();
            AppLog.Error("Audio device could not be opened: " + entry.Name, ex);
            DeviceLost?.Invoke(entry.Id);
        }
    }

    void WatchApplications(string[] selection, ConcurrentDictionary<string, double> gains, MediaClock clock, CaptureSelection? captureSelection, CancellationToken cancellation)
    {
        var trackGame = selection.Contains(AudioApplicationEntry.GameId, StringComparer.OrdinalIgnoreCase);
        var gameId = 0;
        string? gameName = null;
        var retryAt = new Dictionary<string, long>();
        var notified = new HashSet<string>();
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var processes = ApplicationAudioCatalog.Snapshot();
                var currentGame = trackGame ? GameTitles.DetectProcessId(captureSelection) : 0;
                if (currentGame != 0) { gameId = currentGame; gameName = processes.FirstOrDefault(p => p.Id == gameId)?.Name; }
                if (!processes.Any(p => p.Id == gameId && p.Name == gameName)) gameId = 0;
                var targets = ApplicationAudioCatalog.ResolveTargets(selection, processes, gameId);
                var desired = targets.Select(t => t.SlotId).ToHashSet();
                CaptureSlot[] existing;
                lock (_gate) existing = _slots.Where(s => s.Id.StartsWith("app:", StringComparison.Ordinal)).ToArray();
                foreach (var old in existing) if (!desired.Contains(old.Id)) RemoveDevice(old.Id);
                foreach (var stale in retryAt.Keys.Where(key => !desired.Contains(key)).ToArray()) { retryAt.Remove(stale); notified.Remove(stale); }
                foreach (var target in targets)
                {
                    if (cancellation.IsCancellationRequested) break;
                    lock (_gate) if (_slots.Any(s => s.Id == target.SlotId)) continue;
                    if (retryAt.TryGetValue(target.SlotId, out var retry) && Environment.TickCount64 < retry) continue;
                    if (!AddApplicationSlot(target, gains.GetValueOrDefault(target.SourceId, 1), clock, cancellation))
                    {
                        retryAt[target.SlotId] = Environment.TickCount64 + 10_000;
                        if (!cancellation.IsCancellationRequested && notified.Add(target.SlotId)) ApplicationUnavailable?.Invoke(target.Name);
                    }
                    else retryAt.Remove(target.SlotId);
                }
            }
            catch (Exception ex) { AppLog.Error("Audio app discovery failed.", ex); }
            if (cancellation.IsCancellationRequested) break;
            cancellation.WaitHandle.WaitOne(2000);
        }
    }

    bool AddApplicationSlot(ApplicationAudioCatalog.AudioProcessTarget target, double gain, MediaClock clock, CancellationToken cancellation)
    {
        var capture = new ProcessLoopbackCapture(target.Id, clock);
        var slot = new CaptureSlot(target.SlotId, target.Name, false, gain, capture.WaveFormat, target.SourceId) { Capture = capture };
        capture.PacketAvailable += (buffer, count, timestamp) => slot.Append(buffer, count, timestamp);
        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception == null || cancellation.IsCancellationRequested) return;
            AppLog.Error("Audio app stopped: " + target.Name, args.Exception);
            RemoveDevice(slot.Id);
        };
        try
        {
            capture.StartRecording();
            lock (_gate)
            {
                if (cancellation.IsCancellationRequested) { slot.Dispose(); return false; }
                _slots.Add(slot);
                Volatile.Write(ref _activeSlots, _slots.ToArray());
            }
            AppLog.Info($"App audio attached: {target.Name}, PID={target.Id}");
            return true;
        }
        catch (Exception ex) { slot.Dispose(); AppLog.Error("Audio app could not be captured: " + target.Name, ex); return false; }
    }

    void MixLoop()
    {
        using var timer = new CaptureTimer(_mixWake);
        const int frames = 960;
        // WASAPI delivers completed blocks after their position on the media clock.
        // Read a short distance behind capture instead of repeatedly reading future data.
        const long captureLatency = 100 * TimeSpan.TicksPerMillisecond;
        var mixed = new float[frames * 2];
        var channel = new float[frames * 2];
        var pcm = new byte[frames * 2 * 2];
        var levels = new List<AudioLevel>();
        while (_running)
        {
            var start = _mixedFrames * 10_000_000L / Rate;
            var readyAt = start + frames * 10_000_000L / Rate + captureLatency;
            while (_running && (_clock?.Now() ?? readyAt) < readyAt)
                timer.WaitFor(readyAt - (_clock?.Now() ?? readyAt));
            if (!_running)
                break;
            Array.Clear(mixed);
            levels.Clear();
            var slots = Volatile.Read(ref _activeSlots);

            foreach (var slot in slots)
            {
                var got = slot.Read(start, frames, channel);
                var peak = 0f;
                if (got)
                {
                    var gain = slot.Gain;
                    for (var i = 0; i < mixed.Length; i++)
                    {
                        var sample = channel[i] * (float)gain;
                        mixed[i] += sample;
                        var abs = Math.Abs(sample);
                        if (abs > peak)
                            peak = abs;
                    }
                }
                slot.Peak = Math.Max(peak, slot.Peak * 0.82f);
                levels.Add(new AudioLevel
                {
                    DeviceId = slot.GainId,
                    Name = slot.Name,
                    IsMicrophone = slot.Microphone,
                    Peak = Math.Clamp(slot.Peak, 0, 1),
                    Active = got
                });
            }

            for (var i = 0; i < mixed.Length; i++)
            {
                var sample = Math.Tanh(mixed[i]);
                var value = (short)Math.Clamp((int)Math.Round(sample * 32767f), short.MinValue, short.MaxValue);
                pcm[i * 2] = (byte)value;
                pcm[i * 2 + 1] = (byte)(value >> 8);
            }

            Levels = levels.ToArray();
            PcmReady?.Invoke(pcm, start);
            _mixedFrames += frames;

            var target = _clock?.Now() ?? start;
            var ahead = start + frames * 10_000_000L / Rate - target;
            if (ahead > 2_0000)
                Thread.Sleep((int)Math.Min(30, ahead / 10_000));
        }
    }

    public void Stop()
    {
        _applicationStop?.Cancel();
        _applicationThread?.Join(TimeSpan.FromSeconds(6));
        _applicationThread = null;
        _applicationStop?.Dispose();
        _applicationStop = null;
        _applicationGains = null;
        _running = false;
        _mixWake.Set();
        _mixThread?.Join(TimeSpan.FromSeconds(1));
        _mixThread = null;
        List<CaptureSlot> slots;
        lock (_gate)
        {
            slots = _slots.ToList();
            _slots.Clear();
            Volatile.Write(ref _activeSlots, []);
        }
        foreach (var slot in slots)
            slot.Dispose();
        Levels = [];
    }

    public void Dispose()
    {
        Stop();
        try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); } catch { /* already gone */ }
        _enumerator.Dispose();
        _mixWake.Dispose();
    }

    sealed class CaptureSlot : IDisposable
    {
        readonly object _bufferGate = new();
        float[] _samples = new float[Rate * 2];
        float[] _converted = [];
        int _count;
        long _origin = long.MinValue;
        float _resamplePhase;
        float _lastLeft;
        float _lastRight;

        public string Id { get; }
        public string GainId { get; }
        public string Name { get; }
        public bool Microphone { get; }
        public double Gain;
        public volatile bool Mono = true;
        public float Peak;
        public WaveFormat SourceFormat { get; }
        public IWaveIn? Capture;
        public MMDevice? Device;

        public CaptureSlot(string id, string name, bool microphone, double gain, WaveFormat format, string? gainId = null)
        {
            Id = id;
            GainId = gainId ?? id;
            Name = name;
            Microphone = microphone;
            Gain = gain;
            SourceFormat = format;
        }

        public void Append(byte[] buffer, int count, long timestamp)
        {
            if (_disposed)
                return;
            lock (_bufferGate)
            {
                var converted = ToStereo48(buffer, count);
                if (_origin == long.MinValue)
                    _origin = timestamp;
                else
                {
                    var expected = _origin + _count * 10_000_000L / Rate;
                    var gapFrames = (int)((timestamp - expected) * Rate / 10_000_000L);
                    // Loopback can stop sending buffers while an endpoint is silent.
                    // A new burst must retain its real position, even after a long pause.
                    if (gapFrames > Rate)
                    {
                        _origin = timestamp;
                        _count = 0;
                    }
                    else if (gapFrames > Rate / 100)
                        AppendSilence(gapFrames);
                    else if (gapFrames < -converted.Length / 2)
                        return;
                }

                Ensure(_count + converted.Length / 2);
                converted.CopyTo(_samples.AsSpan(_count * 2));
                _count += converted.Length / 2;
                if (_count > Rate * 5)
                {
                    var drop = _count - Rate * 2;
                    Buffer.BlockCopy(_samples, drop * 2 * sizeof(float), _samples, 0, (_count - drop) * 2 * sizeof(float));
                    _count -= drop;
                    _origin += drop * 10_000_000L / Rate;
                }
            }
        }

        public bool Read(long timestamp, int frames, float[] destination)
        {
            lock (_bufferGate)
            {
                Array.Clear(destination);
                if (_origin == long.MinValue || _count == 0)
                    return false;
                var index = (int)((timestamp - _origin) * Rate / 10_000_000L);
                var destinationOffset = Math.Max(0, -index);
                if (destinationOffset >= frames)
                    return false;
                index = Math.Max(0, index);
                if (index >= _count)
                    return false;
                var available = Math.Min(frames - destinationOffset, _count - index);
                Buffer.BlockCopy(_samples, index * 2 * sizeof(float), destination, destinationOffset * 2 * sizeof(float), available * 2 * sizeof(float));
                var consumed = index + available;
                Buffer.BlockCopy(_samples, consumed * 2 * sizeof(float), _samples, 0, (_count - consumed) * 2 * sizeof(float));
                _count -= consumed;
                _origin += consumed * 10_000_000L / Rate;
                return available > 0;
            }
        }

        void AppendSilence(int frames)
        {
            Ensure(_count + frames);
            Array.Clear(_samples, _count * 2, frames * 2);
            _count += frames;
        }

        void Ensure(int frames)
        {
            if (_samples.Length >= frames * 2)
                return;
            var next = new float[Math.Max(frames * 2, _samples.Length * 2)];
            Buffer.BlockCopy(_samples, 0, next, 0, _count * 2 * sizeof(float));
            _samples = next;
        }

        ReadOnlySpan<float> ToStereo48(byte[] buffer, int count)
        {
            var format = SourceFormat;
            var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
            var channels = Math.Max(1, format.Channels);
            var frameSize = bytesPerSample * channels;
            var frames = count / frameSize;
            var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubformat;
            var mono = Microphone && Mono;
            if (!mono && format.SampleRate == Rate && channels == 2 && bytesPerSample == 4 && isFloat)
                return MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, frames * frameSize));
            var capacity = checked(((int)Math.Ceiling(frames * (double)Rate / Math.Max(1, format.SampleRate)) + 4) * 2);
            if (_converted.Length < capacity) Array.Resize(ref _converted, Math.Max(capacity, _converted.Length * 2));
            var written = 0;
            var step = format.SampleRate / (float)Rate;
            for (var i = 0; i < frames; i++)
            {
                var left = ReadSample(buffer, i * frameSize, bytesPerSample, format);
                var right = channels > 1 ? ReadSample(buffer, i * frameSize + bytesPerSample, bytesPerSample, format) : left;
                if (mono && channels > 1)
                {
                    // Interfaces often expose a single mic as stereo with one
                    // silent channel. Center the mic before resampling/mixing;
                    // system and application audio keep their stereo image.
                    var sum = left + right;
                    for (var channel = 2; channel < channels; channel++)
                        sum += ReadSample(buffer, i * frameSize + channel * bytesPerSample, bytesPerSample, format);
                    left = right = sum / channels;
                }
                var position = _resamplePhase;
                if (format.SampleRate == Rate)
                {
                    _converted[written++] = left;
                    _converted[written++] = right;
                }
                else
                {
                    while (position < 1f)
                    {
                        var t = position;
                        _converted[written++] = _lastLeft + (left - _lastLeft) * t;
                        _converted[written++] = _lastRight + (right - _lastRight) * t;
                        position += step;
                    }
                    _resamplePhase = position - 1f;
                    _lastLeft = left;
                    _lastRight = right;
                }
            }
            return _converted.AsSpan(0, written);
        }

        static float ReadSample(byte[] buffer, int offset, int bytes, WaveFormat format)
        {
            if (offset + bytes > buffer.Length)
                return 0;
            var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubformat;
            if (isFloat && bytes >= 4)
                return BitConverter.ToSingle(buffer, offset);
            if (bytes == 1)
                return (buffer[offset] - 128) / 128f;
            if (bytes == 2)
                return BitConverter.ToInt16(buffer, offset) / 32768f;
            if (bytes == 3)
                return ((buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16) << 8 >> 8) / 8388608f;
            if (bytes == 4)
                return BitConverter.ToInt32(buffer, offset) / 2147483648f;
            return 0;
        }

        volatile bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try { Capture?.StopRecording(); } catch { /* already stopped */ }
            Capture?.Dispose();
            Device?.Dispose();
        }
    }

    sealed class DeviceNotifications : IMMNotificationClient
    {
        readonly Action<string> _lost;
        readonly Action<string> _defaultChanged;
        public DeviceNotifications(Action<string> lost, Action<string> defaultChanged)
        {
            _lost = lost;
            _defaultChanged = defaultChanged;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState != DeviceState.Active)
                _lost(deviceId);
        }

        public void OnDeviceAdded(string deviceId) { }
        public void OnDeviceRemoved(string deviceId) => _lost(deviceId);
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => _defaultChanged(defaultDeviceId);
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }
}
