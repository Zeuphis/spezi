using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Spezi.Core.Recording;

namespace Spezi.Core;

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
