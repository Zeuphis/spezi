using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace Spezi.Core.Recording;

internal static class MfBootstrap
{
    static int _started;

    public static void Ensure()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
            MediaFactory.MFStartup();
    }
}

internal static class MfGuids
{
    public static readonly Guid H264 = new(0x34363248, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
    public static readonly Guid Nv12 = new(0x3231564E, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
    public static readonly Guid Rgb32 = new(0x00000016, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
    public static readonly Guid DefaultStride = new("644B4E48-1E02-4516-B0EB-C01CA9D49AC6");
    public static readonly Guid ForceKeyFrame = new("398C1B98-8353-475A-9EF2-8F265D260345");
    public static readonly Guid RateControlMode = new("1C0608E9-370C-4710-8A58-CB6181C42423");
    public static readonly Guid MeanBitRate = new("F7222374-2144-4815-B550-A37F8E12EE52");
    public static readonly Guid GopSize = new("95F31B26-95A4-41AA-9303-246A7FC6EEF1");
    public static readonly Guid LowLatency = new("9C27891A-ED7A-40E1-88E8-B22727A024EE");
    public static readonly Guid BPictureCount = new("8D390AAC-DC5C-4200-B57F-814D04BABAB2");

    public const uint NoEvents = 0xC00D3E80;
    public const uint NeedMoreInput = 0xC00D6D72;
    public const uint StreamChange = 0xC00D6D61;
    public const int EventNoWait = 1;
    public const int OutputProvidesSamples = 0x100;
}

internal static class EncoderCatalog
{
    public static IReadOnlyList<EncoderDescription> Detect(uint preferredVendorId)
    {
        MfBootstrap.Ensure();
        var output = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = MfGuids.H264
        };

        var hardware = Enumerate(
            TransformCategoryGuids.VideoEncoder,
            (uint)EnumFlag.EnumFlagHardware | (uint)EnumFlag.EnumFlagSortandfilter,
            null,
            output);

        var result = new List<EncoderDescription>();
        foreach (var activate in hardware)
        {
            using (activate)
            {
                var name = ReadString(activate, TransformAttributeKeys.MftFriendlyNameAttribute) ?? "Hardware encoder";
                var vendorText = ReadString(activate, TransformAttributeKeys.MftEnumHardwareVendorIdAttribute) ?? "";
                result.Add(new EncoderDescription
                {
                    Name = name,
                    IsHardware = true,
                    VendorId = ParseVendor(vendorText),
                    Vendor = vendorText
                });
            }
        }

        result.Sort((a, b) => Score(b, preferredVendorId).CompareTo(Score(a, preferredVendorId)));
        return result;
    }

    public static bool HasSoftwareEncoder()
    {
        MfBootstrap.Ensure();
        var output = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = MfGuids.H264
        };
        var list = Enumerate(
            TransformCategoryGuids.VideoEncoder,
            (uint)EnumFlag.EnumFlagSyncmft | (uint)EnumFlag.EnumFlagSortandfilter,
            null,
            output);
        var any = list.Count > 0;
        foreach (var item in list)
            item.Dispose();
        return any;
    }

    public static IMFTransform ActivateHardware(uint preferredVendorId, out string name)
    {
        var output = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = MfGuids.H264
        };
        var list = Enumerate(
            TransformCategoryGuids.VideoEncoder,
            (uint)EnumFlag.EnumFlagHardware | (uint)EnumFlag.EnumFlagSortandfilter,
            null,
            output);

        IMFActivate? chosen = null;
        var best = int.MinValue;
        string? chosenName = null;
        foreach (var activate in list)
        {
            var encoderName = ReadString(activate, TransformAttributeKeys.MftFriendlyNameAttribute) ?? "Hardware encoder";
            var vendor = ParseVendor(ReadString(activate, TransformAttributeKeys.MftEnumHardwareVendorIdAttribute));
            var score = Score(new EncoderDescription { Name = encoderName, IsHardware = true, VendorId = vendor }, preferredVendorId);
            if (score > best)
            {
                chosen?.Dispose();
                chosen = activate;
                chosenName = encoderName;
                best = score;
            }
            else
            {
                activate.Dispose();
            }
        }

        if (chosen == null)
            throw new InvalidOperationException("No hardware encoder found.");

        using (chosen)
        {
            name = chosenName ?? "Hardware encoder";
            return chosen.ActivateObject<IMFTransform>();
        }
    }

    public static IMFTransform ActivateSoftware(out string name)
    {
        var output = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = MfGuids.H264
        };
        var list = Enumerate(
            TransformCategoryGuids.VideoEncoder,
            (uint)EnumFlag.EnumFlagSyncmft | (uint)EnumFlag.EnumFlagSortandfilter,
            null,
            output);
        if (list.Count == 0)
            throw new InvalidOperationException("No software encoder found.");

        var chosen = list[0];
        name = ReadString(chosen, TransformAttributeKeys.MftFriendlyNameAttribute) ?? "Software encoder";
        for (var i = 1; i < list.Count; i++)
            list[i].Dispose();
        using (chosen)
        {
            return chosen.ActivateObject<IMFTransform>();
        }
    }

    static int Score(EncoderDescription encoder, uint preferredVendorId)
    {
        var score = 0;
        if (preferredVendorId != 0 && encoder.VendorId == preferredVendorId)
            score += 100;
        var name = encoder.Name.ToUpperInvariant();
        if (name.Contains("NVIDIA")) score += 30;
        if (name.Contains("AMD") || name.Contains("AMF")) score += 20;
        if (name.Contains("INTEL") || name.Contains("QUICK SYNC")) score += 10;
        return score;
    }

    static uint ParseVendor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        var hex = text.StartsWith("VEN_", StringComparison.OrdinalIgnoreCase) ? text[4..] : text;
        return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value) ? value : 0;
    }

    static string? ReadString(IMFActivate activate, Guid key)
    {
        try
        {
            return activate.GetAllocatedString(key);
        }
        catch
        {
            return null;
        }
    }

    static List<IMFActivate> Enumerate(Guid category, uint flags, RegisterTypeInfo? input, RegisterTypeInfo? output)
    {
        MediaFactory.MFTEnumEx(category, flags, input, output, out var array, out var count);
        var list = new List<IMFActivate>((int)count);
        unsafe
        {
            var items = (nint*)array;
            for (uint i = 0; i < count; i++)
                list.Add(new IMFActivate(items[i]));
        }

        if (array != 0)
            Marshal.FreeCoTaskMem(array);
        return list;
    }
}

internal sealed class GraphicsDevice : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public uint VendorId { get; }
    public Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice WinRtDevice { get; }

    GraphicsDevice(ID3D11Device device, ID3D11DeviceContext context, uint vendorId, Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice winRtDevice)
    {
        Device = device;
        Context = context;
        VendorId = vendorId;
        WinRtDevice = winRtDevice;
    }

    public static GraphicsDevice CreateForMonitor(nint monitor)
    {
        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? match = null;
        uint vendor = 0;
        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out var adapter).Success; adapterIndex++)
        {
            var description = adapter.Description1;
            var found = false;
            for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var output).Success; outputIndex++)
            {
                if (output.Description.Monitor == monitor)
                {
                    found = true;
                    vendor = description.VendorId;
                }
                output.Dispose();
                if (found)
                    break;
            }

            if (found)
            {
                match?.Dispose();
                match = adapter;
                break;
            }

            adapter.Dispose();
        }

        ID3D11Device device;
        ID3D11DeviceContext context;
        var flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        if (match != null)
        {
            D3D11CreateDevice(match, DriverType.Unknown, flags, levels, out device, out _, out context).CheckError();
            match.Dispose();
        }
        else
        {
            D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, flags, levels, out device, out _, out context).CheckError();
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var fallbackAdapter = dxgiDevice.GetParent<IDXGIAdapter>();
            vendor = fallbackAdapter.Description.VendorId;
        }

        var multithread = device.QueryInterfaceOrNull<ID3D11Multithread>();
        multithread?.SetMultithreadProtected(true);
        multithread?.Dispose();

        using var dxgi = device.QueryInterface<IDXGIDevice>();
        var hr = Native.CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var punk);
        Marshal.ThrowExceptionForHR(hr);
        var winRt = WinRT.MarshalInterface<Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice>.FromAbi(punk);
        return new GraphicsDevice(device, context, vendor, winRt);
    }

    public void Dispose()
    {
        Context.Dispose();
        Device.Dispose();
    }
}

internal static class CaptureSources
{
    public static IReadOnlyList<DisplayEntry> ListDisplays()
    {
        var list = new List<DisplayEntry>();
        var index = 0;
        Native.EnumDisplayMonitors(0, 0, Collect, 0);
        return list;

        bool Collect(nint monitor, nint hdc, ref Native.Rect rect, nint data)
        {
            var info = new Native.MonitorInfoEx { Size = Marshal.SizeOf<Native.MonitorInfoEx>() };
            if (Native.GetMonitorInfo(monitor, ref info))
            {
                index++;
                var primary = (info.Flags & Native.MonitorInfoPrimary) != 0;
                list.Add(new DisplayEntry
                {
                    DeviceName = info.DeviceName,
                    Name = primary ? $"Display {index} (primary)" : $"Display {index}",
                    MonitorHandle = monitor,
                    Width = info.Monitor.Width,
                    Height = info.Monitor.Height,
                    IsPrimary = primary
                });
            }
            return true;
        }
    }

    public static DisplayEntry ResolveMonitor(string deviceName)
    {
        var displays = ListDisplays();
        if (displays.Count == 0)
            throw new InvalidOperationException("No display found.");

        if (!string.IsNullOrWhiteSpace(deviceName) && !deviceName.Equals("primary", StringComparison.OrdinalIgnoreCase))
        {
            var match = displays.FirstOrDefault(d => d.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        return displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
    }

    public static IReadOnlyList<WindowEntry> ListWindows()
    {
        var list = new List<WindowEntry>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd))
                return true;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DwmCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            var length = Native.GetWindowTextLength(hwnd);
            if (length <= 0)
                return true;
            var buffer = new char[length + 1];
            Native.GetWindowText(hwnd, buffer, buffer.Length);
            var title = new string(buffer).TrimEnd('\0').Trim();
            if (title.Length == 0)
                return true;
            Native.GetWindowRect(hwnd, out var rect);
            if (rect.Width < 320 || rect.Height < 200)
                return true;

            Native.GetWindowThreadProcessId(hwnd, out var processId);
            var processName = "";
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
                return true;
            }

            if (processName.Equals("Spezi", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase))
                return true;

            list.Add(new WindowEntry
            {
                Handle = hwnd,
                Title = title,
                ProcessName = processName
            });
            return true;
        }, 0);

        return list
            .GroupBy(w => w.Handle)
            .Select(g => g.First())
            .OrderBy(w => w.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
