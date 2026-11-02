using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace Spezi.Core.Recording;

internal sealed class EncodedPacket
{
    public bool IsAudio { get; init; }
    public bool IsKeyframe { get; init; }
    public long Timestamp { get; init; }
    public long Duration { get; init; }
    public byte[] Data { get; init; } = [];
}

internal sealed class H264Encoder : IDisposable
{
    readonly object _queueGate = new();
    readonly Queue<PendingFrame> _queue = new();
    readonly AutoResetEvent _signal = new(false);
    IMFTransform? _transform;
    IMFMediaEventGenerator? _events;
    IMFDXGIDeviceManager? _deviceManager;
    Thread? _thread;
    volatile bool _running;
    volatile bool _draining;
    bool _async;
    bool _hardware;
    int _fps;
    int _forceKeyframe;
    long _frameDuration;
    long _submitted;
    long _lastKeyframeRequest = long.MinValue;
    CodecApi? _codec;
    int _outputSize = 512 * 1024;

    public string Name { get; private set; } = "";
    public bool IsHardware => _hardware;
    public IMFMediaType? OutputType { get; private set; }
    public event Action<EncodedPacket>? Packet;
    public long DroppedFrames;

    public void Start(ID3D11Device device, int width, int height, int fps, int bitrateKbps, bool allowSoftware, uint vendorId)
    {
        Stop();
        MfBootstrap.Ensure();
        _fps = fps;
        _frameDuration = 10_000_000L / fps;
        _hardware = true;
        try
        {
            _transform = EncoderCatalog.ActivateHardware(vendorId, out var name);
            Name = name;
            _async = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Hardware encoder activation failed.", ex);
            if (!allowSoftware)
                throw new InvalidOperationException("No hardware encoder is available. Software encoding is disabled because it uses significantly more CPU.");
            _hardware = false;
            _async = false;
            _transform = EncoderCatalog.ActivateSoftware(out var softwareName);
            Name = softwareName;
        }

        if (_async)
        {
            var attributes = _transform.Attributes;
            attributes.Set(TransformAttributeKeys.TransformAsyncUnlock, 1u);
            var manager = MediaFactory.MFCreateDXGIDeviceManager();
            manager.ResetDevice(device);
            _deviceManager = manager;
            _transform.ProcessMessage(TMessageType.MessageSetD3DManager, (UIntPtr)(ulong)manager.NativePointer);
        }

        _submitted = 0;
        _lastKeyframeRequest = long.MinValue;
        TrySetCodecApi(bitrateKbps * 1000, fps);
        ConfigureTypes(width, height, fps, bitrateKbps * 1000);
        TrySetCodecApi(bitrateKbps * 1000, fps);
        OutputType = CloneType(_transform.GetOutputCurrentType(0));
        _events = _async ? _transform.QueryInterface<IMFMediaEventGenerator>() : null;
        _transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
        _transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);

        _running = true;
        _draining = false;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Spezi H264" };
        _thread.Start();
        AppLog.Info($"Encoder started: {Name}, hardware={_hardware}, {width}x{height}@{fps}, {bitrateKbps} kbps");
    }

    public void RequestKeyframe() => Interlocked.Exchange(ref _forceKeyframe, 1);

    public void SubmitTexture(ID3D11Texture2D texture, long timestamp, bool forceKeyframe)
    {
        if (!_running || !_hardware)
        {
            texture.Dispose();
            return;
        }
        lock (_queueGate)
        {
            if (_queue.Count >= 4)
            {
                DroppedFrames++;
                texture.Dispose();
                return;
            }
            _queue.Enqueue(new PendingFrame(texture, null, timestamp, forceKeyframe));
        }
        _signal.Set();
    }

    public void SubmitNv12(byte[] nv12, long timestamp, bool forceKeyframe)
    {
        if (!_running || _hardware)
            return;
        lock (_queueGate)
        {
            if (_queue.Count >= 3)
            {
                DroppedFrames++;
                return;
            }
            _queue.Enqueue(new PendingFrame(null, nv12, timestamp, forceKeyframe));
        }
        _signal.Set();
    }

    public void DrainAndStop()
    {
        if (!_running)
            return;
        _draining = true;
        _signal.Set();
        _thread?.Join(TimeSpan.FromSeconds(5));
        Cleanup();
    }

    public void Stop()
    {
        _running = false;
        _draining = false;
        _signal.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        Cleanup();
    }

    void ConfigureTypes(int width, int height, int fps, int bitrate)
    {
        var transform = _transform ?? throw new InvalidOperationException();
        var output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        output.Set(MediaTypeAttributeKeys.Subtype, MfGuids.H264);
        MediaFactory.MFSetAttributeSize(output, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
        MediaFactory.MFSetAttributeRatio(output, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1);
        MediaFactory.MFSetAttributeRatio(output, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        output.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        output.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
        output.Set(MediaTypeAttributeKeys.Mpeg2Profile, 100u);
        output.Set(MediaTypeAttributeKeys.MaxKeyframeSpacing, (uint)(fps * 2));
        output.Set(MediaTypeAttributeKeys.AllSamplesIndependent, 0u);
        transform.SetOutputType(0, output, 0);
        output.Dispose();

        IMFMediaType? input = null;
        for (var i = 0; i < 8; i++)
        {
            IMFMediaType candidate;
            try
            {
                candidate = transform.GetInputAvailableType(0, i);
            }
            catch
            {
                break;
            }

            candidate.GetGUID(MediaTypeAttributeKeys.Subtype, out var subtype);
            if (subtype == MfGuids.Nv12)
            {
                input = candidate;
                break;
            }
            candidate.Dispose();
        }

        input ??= MediaFactory.MFCreateMediaType();
        input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        input.Set(MediaTypeAttributeKeys.Subtype, MfGuids.Nv12);
        MediaFactory.MFSetAttributeSize(input, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
        MediaFactory.MFSetAttributeRatio(input, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1);
        MediaFactory.MFSetAttributeRatio(input, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        input.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        transform.SetInputType(0, input, 0);
        input.Dispose();
    }

    void TrySetCodecApi(int bitrate, int fps)
    {
        if (_transform == null)
            return;
        try
        {
            _codec ??= QueryCodec(_transform);
            TrySet(_codec, MfGuids.RateControlMode, 0u);
            TrySet(_codec, MfGuids.MeanBitRate, (uint)bitrate);
            TrySet(_codec, MfGuids.GopSize, (uint)(fps * 2));
            TrySet(_codec, MfGuids.BPictureCount, 0u);
            TrySet(_codec, MfGuids.LowLatency, 1u, boolean: true);
        }
        catch (Exception ex)
        {
            AppLog.Info("Codec API properties were not applied: " + ex.Message);
        }
    }

    static CodecApi QueryCodec(IMFTransform transform)
    {
        var iid = new Guid("901DB4C7-31CE-41A2-85DC-8FA0BF41B8DA");
        var hr = Marshal.QueryInterface(transform.NativePointer, in iid, out var pointer);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
        return new CodecApi(pointer);
    }

    static bool TrySet(CodecApi codec, Guid key, uint value, bool boolean = false)
    {
        var property = key;
        var variant = new Variant32 { Vt = boolean ? (ushort)11 : (ushort)19, Value = value };
        if (boolean)
            variant.BooleanValue = value == 0 ? (short)0 : (short)-1;
        var hr = codec.SetValue(ref property, ref variant);
        if (hr < 0)
            AppLog.Info($"Optional codec property {key} was not applied: 0x{hr:X8}");
        return hr >= 0;
    }

    void Loop()
    {
        try
        {
            if (_async)
                AsyncLoop();
            else
                SyncLoop();
        }
        catch (Exception ex)
        {
            AppLog.Error("Encoder loop failed.", ex);
        }
    }

    void AsyncLoop()
    {
        var credits = 0;
        var endSent = false;
        while (_running || _draining)
        {
            while (credits > 0 && TryDequeue(out var frame))
            {
                Submit(frame);
                credits--;
            }

            if (_draining && !endSent && credits >= 0 && !HasQueued())
            {
                _transform?.ProcessMessage(TMessageType.MessageNotifyEndOfStream, UIntPtr.Zero);
                _transform?.ProcessMessage(TMessageType.MessageCommandDrain, UIntPtr.Zero);
                endSent = true;
            }

            if (!TryGetEvent(out var mediaEvent))
            {
                if (!_running && !_draining)
                    break;
                _signal.WaitOne(10);
                continue;
            }

            using (mediaEvent)
            {
                switch (mediaEvent!.EventType)
                {
                    case MediaEventTypes.TransformNeedInput:
                        credits++;
                        break;
                    case MediaEventTypes.TransformHaveOutput:
                        PullOutput();
                        break;
                    case MediaEventTypes.TransformDrainComplete:
                        return;
                    case MediaEventTypes.TransformUnknown:
                        break;
                }
            }

            if (endSent && !_draining)
                break;
        }
    }

    void SyncLoop()
    {
        while (_running || _draining)
        {
            if (!TryDequeue(out var frame))
            {
                if (_draining)
                {
                    _transform?.ProcessMessage(TMessageType.MessageCommandDrain, UIntPtr.Zero);
                    PullOutput();
                    return;
                }
                _signal.WaitOne(10);
                continue;
            }

            Submit(frame);
            PullOutput();
        }
    }

    void Submit(PendingFrame frame)
    {
        var transform = _transform!;
        IMFSample sample;
        if (frame.Texture != null)
        {
            var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(Native.Texture2DIid, frame.Texture, 0, false);
            sample = MediaFactory.MFCreateSample();
            sample.AddBuffer(buffer);
            buffer.Dispose();
        }
        else
        {
            var data = frame.Bytes!;
            var buffer = MediaFactory.MFCreateMemoryBuffer(data.Length);
            WriteBuffer(buffer, data);
            sample = MediaFactory.MFCreateSample();
            sample.AddBuffer(buffer);
            buffer.Dispose();
        }

        sample.SampleTime = frame.Timestamp;
        sample.SampleDuration = _frameDuration;
        try
        {
            var force = Interlocked.Exchange(ref _forceKeyframe, 0) == 1;
            if (frame.ForceKeyframe || force || _submitted == 0 ||
                frame.Timestamp - _lastKeyframeRequest >= 2 * TimeSpan.TicksPerSecond)
            {
                if (_codec != null && TrySet(_codec, MfGuids.ForceKeyFrame, 1u))
                    _lastKeyframeRequest = frame.Timestamp;
            }
            transform.ProcessInput(0, sample, 0);
            _submitted++;
        }
        finally
        {
            sample.Dispose();
            frame.Texture?.Dispose();
        }
    }

    void PullOutput()
    {
        var transform = _transform!;
        while (true)
        {
            var info = transform.GetOutputStreamInfo(0);
            if (info.Size > 0)
                _outputSize = (int)info.Size;
            var data = new OutputDataBuffer { StreamID = 0 };
            IMFSample? provided = null;
            if (((int)info.Flags & MfGuids.OutputProvidesSamples) == 0)
            {
                provided = MediaFactory.MFCreateSample();
                var memory = MediaFactory.MFCreateMemoryBuffer(Math.Max(_outputSize, 256 * 1024));
                provided.AddBuffer(memory);
                memory.Dispose();
                data.Sample = provided;
            }

            Result result;
            ProcessOutputStatus status;
            try
            {
                result = transform.ProcessOutput(ProcessOutputFlags.None, 1, ref data, out status);
            }
            catch (SharpGenException ex) when (IsHr(ex, MfGuids.NeedMoreInput))
            {
                provided?.Dispose();
                return;
            }

            if (result.Failure)
            {
                provided?.Dispose();
                if ((uint)result.Code == MfGuids.NeedMoreInput)
                    return;
                if ((uint)result.Code == MfGuids.StreamChange)
                {
                    OutputType?.Dispose();
                    OutputType = CloneType(transform.GetOutputCurrentType(0));
                    continue;
                }
                if ((uint)result.Code == 0x8000FFFF)
                    return;
                AppLog.Error("ProcessOutput failed: " + result.Code);
                return;
            }

            var sample = data.Sample ?? provided;
            try
            {
                if (sample != null)
                    Emit(sample);
            }
            finally
            {
                if (sample != null && sample.NativePointer != provided?.NativePointer)
                    sample.Dispose();
                data.Events?.Dispose();
                provided?.Dispose();
            }
            // An asynchronous MFT grants one ProcessOutput call per HaveOutput event.
            if (_async)
                return;
        }
    }

    void Emit(IMFSample sample)
    {
        try
        {
            using var buffer = sample.ConvertToContiguousBuffer();
            var bytes = ReadBuffer(buffer);
            if (bytes.Length == 0)
                return;
            var key = false;
            try
            {
                key = sample.GetUInt32(SampleAttributeKeys.CleanPoint, out var clean).Success && clean != 0;
            }
            catch
            {
                key = false;
            }

            long timestamp = 0;
            long duration = _frameDuration;
            timestamp = sample.SampleTime;
            duration = sample.SampleDuration > 0 ? sample.SampleDuration : _frameDuration;
            Packet?.Invoke(new EncodedPacket
            {
                IsAudio = false,
                IsKeyframe = key,
                Timestamp = timestamp,
                Duration = duration > 0 ? duration : _frameDuration,
                Data = bytes
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not copy an encoded video packet.", ex);
        }
    }

    bool TryGetEvent(out IMFMediaEvent? mediaEvent)
    {
        mediaEvent = null;
        if (_events == null)
            return false;
        try
        {
            mediaEvent = _events.GetEvent(MfGuids.EventNoWait);
            return mediaEvent != null;
        }
        catch (Exception ex) when (IsHr(ex, MfGuids.NoEvents))
        {
            return false;
        }
    }

    static bool IsHr(Exception exception, uint code)
    {
        if ((uint)exception.HResult == code)
            return true;
        if (exception is SharpGenException sharp && (uint)sharp.ResultCode.Code == code)
            return true;
        return false;
    }

    bool TryDequeue(out PendingFrame frame)
    {
        lock (_queueGate)
        {
            if (_queue.Count == 0)
            {
                frame = default;
                return false;
            }
            frame = _queue.Dequeue();
            return true;
        }
    }

    bool HasQueued()
    {
        lock (_queueGate)
            return _queue.Count > 0;
    }

    static IMFMediaType CloneType(IMFMediaType source)
    {
        var copy = MediaFactory.MFCreateMediaType();
        source.CopyAllItems(copy);
        return copy;
    }

    public static byte[] ReadBuffer(IMFMediaBuffer buffer, int maximumLength = 8 * 1024 * 1024)
    {
        nint pointer = 0;
        var max = 0;
        var current = 0;
        buffer.Lock(out pointer, out max, out current);
        try
        {
            var reported = buffer.CurrentLength;
            if (reported > 0 && reported <= max)
                current = reported;
            if (current < 0 || current > max || current > maximumLength)
            {
                AppLog.Error($"Media buffer length rejected: current={current} max={max} reported={reported}");
                return [];
            }
            var data = new byte[current];
            if (current > 0 && pointer != 0)
                Marshal.Copy(pointer, data, 0, current);
            return data;
        }
        finally
        {
            buffer.Unlock();
        }
    }

    public static void WriteBuffer(IMFMediaBuffer buffer, byte[] data) => WriteBuffer(buffer, data.AsSpan());

    public static unsafe void WriteBuffer(IMFMediaBuffer buffer, ReadOnlySpan<byte> data)
    {
        nint pointer = 0;
        var max = 0;
        var current = 0;
        buffer.Lock(out pointer, out max, out current);
        try
        {
            if (data.Length > max || pointer == 0 && data.Length > 0)
                throw new InvalidOperationException("The media buffer is smaller than its input.");
            data.CopyTo(new Span<byte>((void*)pointer, data.Length));
            buffer.CurrentLength = data.Length;
        }
        finally
        {
            buffer.Unlock();
        }
    }

    void Cleanup()
    {
        _running = false;
        lock (_queueGate)
        {
            while (_queue.Count > 0)
            {
                var pending = _queue.Dequeue();
                pending.Texture?.Dispose();
            }
        }
        OutputType?.Dispose();
        OutputType = null;
        _events?.Dispose();
        _events = null;
        if (_codec != null)
        {
            _codec.Dispose();
            _codec = null;
        }
        _transform?.Dispose();
        _transform = null;
        _deviceManager?.Dispose();
        _deviceManager = null;
    }

    public void Dispose() => Stop();

    readonly record struct PendingFrame(ID3D11Texture2D? Texture, byte[]? Bytes, long Timestamp, bool ForceKeyframe);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct Variant32
    {
        [FieldOffset(0)]
        public ushort Vt;
        [FieldOffset(8)]
        public uint Value;
        [FieldOffset(8)]
        public short BooleanValue;
    }

    // Use the same native COM access as IMFTransform. A runtime callable wrapper
    // created on WPF's STA thread tries to marshal this driver interface when
    // the encoder worker calls it; drivers need not expose a custom proxy.
    sealed class CodecApi : IDisposable
    {
        nint _pointer;
        readonly SetValueDelegate _setValue;

        public CodecApi(nint pointer)
        {
            _pointer = pointer;
            var vtable = Marshal.ReadIntPtr(pointer);
            // IUnknown's three slots, then six ICodecAPI methods before SetValue.
            _setValue = Marshal.GetDelegateForFunctionPointer<SetValueDelegate>(
                Marshal.ReadIntPtr(vtable, 9 * IntPtr.Size));
        }

        public int SetValue(ref Guid api, ref Variant32 value) =>
            _setValue(_pointer, ref api, ref value);

        public void Dispose()
        {
            var pointer = Interlocked.Exchange(ref _pointer, 0);
            if (pointer != 0)
                Marshal.Release(pointer);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int SetValueDelegate(nint instance, ref Guid api, ref Variant32 value);
    }
}
