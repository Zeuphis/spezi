using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace Spezi.Core.Recording;

internal static class Native
{
    static readonly Lazy<Task<bool>> BorderlessAccess = new(RequestBorderlessAccessAsync);

    static async Task<bool> RequestBorderlessAccessAsync()
    {
        nint name = 0, factory = 0, operation = 0;
        try
        {
            const string className = "Windows.Graphics.Capture.GraphicsCaptureAccess";
            Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out name));
            var iid = new Guid("743ed370-06ec-5040-a58a-901f0f757095");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref iid, out factory));
            var request = Marshal.GetDelegateForFunctionPointer<RequestCaptureAccess>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 6 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(request(factory, 0, out operation));
            var access = WinRT.MarshalInterface<Windows.Foundation.IAsyncOperation<Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus>>.FromAbi(operation);
            var status = await access.AsTask().ConfigureAwait(false);
            AppLog.Info("Borderless capture access: " + status);
            return status == Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.Allowed;
        }
        catch (Exception ex)
        {
            AppLog.Info("Borderless capture access unavailable: " + ex.Message);
            return false;
        }
        finally
        {
            if (operation != 0) Marshal.Release(operation);
            if (factory != 0) Marshal.Release(factory);
            if (name != 0) WindowsDeleteString(name);
        }
    }

    public static bool TryDisableCaptureBorder(GraphicsCaptureSession session)
    {
        nint pointer = 0;
        try
        {
            var access = BorderlessAccess.Value;
            if (!access.Wait(TimeSpan.FromSeconds(2)) || !access.Result)
                return false;
            var iid = new Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be");
            var unknown = ((WinRT.IWinRTObject)session).NativeObject.ThisPtr;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out pointer));
            var table = Marshal.ReadIntPtr(pointer);
            var set = Marshal.GetDelegateForFunctionPointer<SetBorderRequired>(Marshal.ReadIntPtr(table, 7 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(set(pointer, 0));
            var get = Marshal.GetDelegateForFunctionPointer<GetBorderRequired>(Marshal.ReadIntPtr(table, 6 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(get(pointer, out var required));
            AppLog.Info("Capture border required: " + (required != 0));
            return required == 0;
        }
        catch (Exception ex)
        {
            AppLog.Info("Capture border could not be disabled: " + ex.Message);
            return false;
        }
        finally { if (pointer != 0) Marshal.Release(pointer); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int RequestCaptureAccess(nint instance, int kind, out nint operation);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int SetBorderRequired(nint instance, byte required);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetBorderRequired(nint instance, out byte required);

    public static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    public static readonly Guid Texture2DIid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [DllImport("combase.dll")]
    public static extern int RoGetActivationFactory(nint classId, ref Guid iid, out nint factory);

    [DllImport("combase.dll", PreserveSig = true)]
    public static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, int length, out nint hstring);

    [DllImport("combase.dll")]
    public static extern int WindowsDeleteString(nint hstring);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [DllImport("kernel32.dll")]
    public static extern bool QueryPerformanceCounter(out long value);

    [DllImport("kernel32.dll")]
    public static extern bool QueryPerformanceFrequency(out long value);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc proc, nint param);

    public delegate bool EnumWindowsProc(nint hwnd, nint param);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hwnd, char[] text, int count);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(nint hwnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorProc proc, nint data);

    public delegate bool MonitorProc(nint monitor, nint hdc, ref Rect rect, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    public const int DwmCloaked = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    public const uint MonitorInfoPrimary = 1;

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(nint window, ref Guid iid, out nint result);

        [PreserveSig]
        int CreateForMonitor(nint monitor, ref Guid iid, out nint result);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface(ref Guid iid, out nint result);
    }

    public static GraphicsCaptureItem CreateCaptureItemForMonitor(nint monitor)
    {
        var interop = GetCaptureFactory();
        var iid = GraphicsCaptureItemIid;
        var hr = interop.CreateForMonitor(monitor, ref iid, out var pointer);
        Marshal.ThrowExceptionForHR(hr);
        return GraphicsCaptureItem.FromAbi(pointer);
    }

    public static GraphicsCaptureItem CreateCaptureItemForWindow(nint window)
    {
        var interop = GetCaptureFactory();
        var iid = GraphicsCaptureItemIid;
        var hr = interop.CreateForWindow(window, ref iid, out var pointer);
        Marshal.ThrowExceptionForHR(hr);
        return GraphicsCaptureItem.FromAbi(pointer);
    }

    static IGraphicsCaptureItemInterop GetCaptureFactory()
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        var hr = WindowsCreateString(className, className.Length, out var name);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var iid = typeof(IGraphicsCaptureItemInterop).GUID;
            hr = RoGetActivationFactory(name, ref iid, out var pointer);
            Marshal.ThrowExceptionForHR(hr);
            var factory = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);
            return factory;
        }
        finally
        {
            WindowsDeleteString(name);
        }
    }
}

internal sealed class MediaClock
{
    readonly long _frequency;
    readonly long _origin100Ns;

    public MediaClock()
    {
        Native.QueryPerformanceFrequency(out _frequency);
        Native.QueryPerformanceCounter(out var start);
        _origin100Ns = CounterToTicks(start, _frequency);
    }

    public long Now()
    {
        Native.QueryPerformanceCounter(out var now);
        return CounterToTicks(now, _frequency) - _origin100Ns;
    }

    internal static long CounterToTicks(long counter, long frequency) =>
        checked((long)((Int128)counter * TimeSpan.TicksPerSecond / frequency));

    public long FromSystemRelative(TimeSpan systemRelative) => systemRelative.Ticks - _origin100Ns;
}
