using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Spezi.Core.Recording;

// Windows process loopback includes a process tree across all output devices.
// API reference: learn.microsoft.com/windows/win32/api/audioclientactivationparams/
internal sealed class ProcessLoopbackCapture : IWaveIn
{
    readonly int _processId;
    readonly MediaClock _clock;
    readonly AutoResetEvent _wake = new(false);
    readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Thread? _thread;
    volatile bool _stop;
    bool _disposed;
    readonly WaveFormat _format = new(48000, 16, 2);

    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;
    internal event Action<byte[], int, long>? PacketAvailable;
    public WaveFormat WaveFormat { get => _format; set => throw new NotSupportedException("The process capture format is fixed."); }

    public ProcessLoopbackCapture(int processId, MediaClock clock) { _processId = processId; _clock = clock; }

    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSupported) throw new NotSupportedException("App audio requires Windows 11 or Windows build 20348 or later.");
        if (_thread != null) throw new InvalidOperationException("Capture already started.");
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "Spezi App Audio " + _processId };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        try { _started.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch { StopRecording(); throw; }
    }

    void CaptureLoop()
    {
        IProcessAudioClient? client = null;
        IProcessAudioCaptureClient? capture = null;
        ActivationCallback? activation = null;
        Exception? error = null;
        var initialized = CoInitializeEx(0, 0) >= 0;
        try
        {
            // Initialize inside the MTA completion callback, matching Microsoft's
            // application-loopback sample. Keep activation memory until the client closes.
            (client, activation) = Activate(_processId, activatedClient =>
            {
                var format = new NativeWaveFormat { FormatTag = 1, Channels = 2, SamplesPerSecond = 48000, BitsPerSample = 16, BlockAlign = 4, AverageBytesPerSecond = 192000 };
                // LOOPBACK | EVENTCALLBACK | AUTOCONVERTPCM | SRC_DEFAULT_QUALITY.
                Marshal.ThrowExceptionForHR(activatedClient.Initialize(0, 0x88060000, 0, 0, ref format, 0));
                Marshal.ThrowExceptionForHR(activatedClient.GetService(typeof(IProcessAudioCaptureClient).GUID, out var service));
                capture = (IProcessAudioCaptureClient)service;
                Marshal.ThrowExceptionForHR(activatedClient.SetEventHandle(_wake.SafeWaitHandle.DangerousGetHandle()));
            });
            var captureClient = capture ?? throw new InvalidOperationException("Windows did not provide an audio capture client.");
            Marshal.ThrowExceptionForHR(client.Start());
            _started.TrySetResult();
            var buffer = new byte[4800 * _format.BlockAlign];
            var firstPacket = true;
            while (!_stop)
            {
                _wake.WaitOne(1000);
                while (!_stop)
                {
                    Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out var next));
                    if (next == 0) break;
                    Marshal.ThrowExceptionForHR(captureClient.GetBuffer(out var data, out var frames, out var flags, out _, out var qpc));
                    if (frames == 0) break;
                    try
                    {
                        var bytes = checked((int)frames * _format.BlockAlign);
                        if (buffer.Length < bytes) Array.Resize(ref buffer, bytes);
                        if ((flags & 2) != 0 || data == 0) Array.Clear(buffer, 0, bytes);
                        else Marshal.Copy(data, buffer, 0, bytes);
                        var timestamp = (flags & 4) == 0 && qpc > 0 && qpc <= long.MaxValue
                            ? _clock.FromSystemRelative(TimeSpan.FromTicks((long)qpc))
                            : _clock.Now() - frames * TimeSpan.TicksPerSecond / _format.SampleRate;
                        if (firstPacket)
                        {
                            AppLog.Info($"App audio first packet: PID={_processId}, timestampMs={timestamp / 10000}, clockMs={_clock.Now() / 10000}, flags={flags}");
                            firstPacket = false;
                        }
                        PacketAvailable?.Invoke(buffer, bytes, timestamp);
                        DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));
                    }
                    finally { Marshal.ThrowExceptionForHR(captureClient.ReleaseBuffer(frames)); }
                }
            }
        }
        catch (Exception ex) { error = ex; _started.TrySetException(ex); }
        finally
        {
            if (client != null) { try { client.Stop(); } catch (Exception ex) { AppLog.Error("Loopback client stop failed.", ex); } }
            if (capture != null) Marshal.ReleaseComObject(capture);
            if (client != null) Marshal.ReleaseComObject(client);
            activation?.ReleaseParameters();
            if (initialized) CoUninitialize();
            RecordingStopped?.Invoke(this, new StoppedEventArgs(_stop ? null : error));
        }
    }

    static (IProcessAudioClient Client, ActivationCallback Activation) Activate(int processId, Action<IProcessAudioClient> initialize)
    {
        var callback = new ActivationCallback(processId, initialize);
        IProcessAudioActivationOperation? operation = null;
        try
        {
            var variant = callback.Parameters;
            var iid = typeof(IProcessAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, ref variant, callback, out operation));
            var client = callback.Completion.Task.WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult();
            return (client, callback);
        }
        catch { callback.Abandon(); throw; }
        finally { if (operation != null) Marshal.ReleaseComObject(operation); GC.KeepAlive(callback); }
    }

    public void StopRecording() { _stop = true; _wake.Set(); if (_thread != Thread.CurrentThread) _thread?.Join(TimeSpan.FromSeconds(6)); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopRecording();
        if (_thread == null || !_thread.IsAlive) _wake.Dispose();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class ActivationCallback : IProcessAudioActivationCallback, IProcessAudioAgileObject
    {
        readonly object _gate = new();
        readonly Action<IProcessAudioClient> _initialize;
        nint _blob;
        bool _abandoned;
        public readonly TaskCompletionSource<IProcessAudioClient> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ProcessAudioPropVariant Parameters { get; }

        public ActivationCallback(int processId, Action<IProcessAudioClient> initialize)
        {
            _initialize = initialize;
            var parameters = new ProcessAudioActivationParams { ActivationType = 1, ProcessId = (uint)processId, Mode = 0 };
            _blob = Marshal.AllocCoTaskMem(Marshal.SizeOf<ProcessAudioActivationParams>());
            Marshal.StructureToPtr(parameters, _blob, false);
            Parameters = new ProcessAudioPropVariant { VariantType = 65, BlobSize = (uint)Marshal.SizeOf<ProcessAudioActivationParams>(), BlobData = _blob };
        }

        public int ActivateCompleted(IProcessAudioActivationOperation operation)
        {
            lock (_gate)
            {
                object? activated = null;
                try
                {
                    Marshal.ThrowExceptionForHR(operation.GetActivateResult(out var result, out activated));
                    Marshal.ThrowExceptionForHR(result);
                    if (_abandoned) { if (activated != null) Marshal.ReleaseComObject(activated); }
                    else
                    {
                        var client = (IProcessAudioClient)activated!;
                        _initialize(client);
                        Completion.TrySetResult(client);
                    }
                }
                catch (Exception ex) { if (activated != null) Marshal.ReleaseComObject(activated); Completion.TrySetException(ex); }
                finally { if (_abandoned || Completion.Task.IsFaulted) FreeBlob(); }
            }
            return 0;
        }

        public void Abandon()
        {
            lock (_gate)
            {
                _abandoned = true;
                if (Completion.Task.IsCompletedSuccessfully) { Marshal.ReleaseComObject(Completion.Task.Result); FreeBlob(); }
            }
        }
        public void ReleaseParameters() { lock (_gate) FreeBlob(); }
        void FreeBlob() { if (_blob != 0) { Marshal.FreeCoTaskMem(_blob); _blob = 0; } }
        ~ActivationCallback() { FreeBlob(); }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid iid,
        ref ProcessAudioPropVariant parameters, IProcessAudioActivationCallback callback, out IProcessAudioActivationOperation operation);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessAudioActivationParams { public int ActivationType; public uint ProcessId; public int Mode; }
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct ProcessAudioPropVariant { [FieldOffset(0)] public ushort VariantType; [FieldOffset(8)] public uint BlobSize; [FieldOffset(16)] public nint BlobData; }
[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct NativeWaveFormat
{
    public ushort FormatTag, Channels;
    public uint SamplesPerSecond, AverageBytesPerSecond;
    public ushort BlockAlign, BitsPerSample, ExtraSize;
}

[ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProcessAudioAgileObject { }
[ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProcessAudioActivationCallback { [PreserveSig] int ActivateCompleted(IProcessAudioActivationOperation operation); }
[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IProcessAudioActivationOperation { [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object activated); }
[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IProcessAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint flags, long bufferDuration, long periodicity, ref NativeWaveFormat format, nint session);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closest);
    [PreserveSig] int GetMixFormat(out nint format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(nint handle);
    [PreserveSig] int GetService([In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}
[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IProcessAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}
