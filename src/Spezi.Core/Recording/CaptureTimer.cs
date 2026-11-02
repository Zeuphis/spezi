using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Spezi.Core.Recording;

// A per-thread high-resolution timer avoids busy waiting and global timer changes.
internal sealed class CaptureTimer : WaitHandle
{
    readonly WaitHandle[] _waits;

    public CaptureTimer(WaitHandle wake)
    {
        var handle = CreateWaitableTimerEx(0, null, 0x2, 0x1F0003);
        if (handle == 0)
            handle = CreateWaitableTimerEx(0, null, 0, 0x1F0003);
        if (handle == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        SafeWaitHandle = new SafeWaitHandle(handle, true);
        _waits = [this, wake];
    }

    public void WaitFor(long ticks)
    {
        var due = -Math.Max(1, ticks);
        if (!SetWaitableTimer(SafeWaitHandle, ref due, 0, 0, 0, false))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        WaitAny(_waits);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern nint CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        nint completion, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
