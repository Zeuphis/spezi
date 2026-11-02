using System.Runtime.InteropServices;
using System.Windows.Threading;
using Spezi.Core;

namespace Spezi.App;

// Used only for Page Up / Page Down if another application owns RegisterHotKey.
// A sleeping message-loop thread keeps recording work out of the hook callback.
sealed class PageKeyFallback : IDisposable
{
    sealed record Binding(int Id, int Key, uint Modifiers, long Token);
    Binding[] _bindings = [];
    long _token;
    Thread? _thread;
    Dispatcher? _dispatcher;
    volatile bool _stopping;
    bool _upPressed;
    bool _downPressed;
    readonly KeyboardHook _callback;
    public event Action<int, long>? Pressed;

    public PageKeyFallback() => _callback = OnKey;

    public bool TryRegister(int id, HotkeyBinding binding, out int error)
    {
        if (_thread != null && _stopping)
        {
            Stop();
            if (_thread != null) { error = 1460; return false; }
        }
        if (_thread == null)
        {
            _stopping = false;
            var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(() =>
            {
                nint hook = 0;
                try
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
                    ready.TrySetResult(hook == 0 ? Marshal.GetLastWin32Error() : 0);
                    if (hook != 0 && !_stopping) Dispatcher.Run();
                }
                catch (Exception ex) { AppLog.Error("Page-key listener failed.", ex); ready.TrySetResult(-1); }
                finally { if (hook != 0) UnhookWindowsHookEx(hook); }
            }) { IsBackground = true, Name = "Spezi Page Hotkeys" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            error = ready.Task.Wait(TimeSpan.FromSeconds(3)) ? ready.Task.Result : 1460;
            if (error != 0) { Stop(); return false; }
        }
        error = 0;
        var entries = Volatile.Read(ref _bindings).Where(entry => entry.Id != id).ToList();
        entries.Add(new(id, binding.VirtualKey, binding.Modifiers, ++_token));
        Volatile.Write(ref _bindings, entries.ToArray());
        return true;
    }

    public bool IsCurrent(int id, long token) => Volatile.Read(ref _bindings).Any(entry => entry.Id == id && entry.Token == token);

    public void Unregister(int id)
    {
        var entries = Volatile.Read(ref _bindings).Where(entry => entry.Id != id).ToArray();
        Volatile.Write(ref _bindings, entries);
        if (entries.Length == 0) Stop();
    }

    nint OnKey(int code, nint message, nint data)
    {
        if (code < 0 || _stopping) return CallNextHookEx(0, code, message, data);
        var key = Marshal.ReadInt32(data);
        if (key is not (0x21 or 0x22)) return CallNextHookEx(0, code, message, data);
        ref var held = ref (key == 0x21 ? ref _upPressed : ref _downPressed);
        if (message is 0x0101 or 0x0105)
        {
            var consumed = held;
            held = false;
            return consumed ? 1 : CallNextHookEx(0, code, message, data);
        }
        if (message is not (0x0100 or 0x0104)) return CallNextHookEx(0, code, message, data);
        if (held) return 1;
        uint modifiers = 0;
        // The current Page key has not updated yet; the other modifier keys have.
        if (GetAsyncKeyState(0x11) < 0) modifiers |= HotkeyModifiers.Control;
        if (GetAsyncKeyState(0x12) < 0) modifiers |= HotkeyModifiers.Alt;
        if (GetAsyncKeyState(0x10) < 0) modifiers |= HotkeyModifiers.Shift;
        if (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) modifiers |= HotkeyModifiers.Win;
        foreach (var binding in Volatile.Read(ref _bindings))
        {
            if (binding.Key != key || binding.Modifiers != modifiers) continue;
            held = true;
            Pressed?.Invoke(binding.Id, binding.Token);
            return 1;
        }
        return CallNextHookEx(0, code, message, data);
    }

    void Stop()
    {
        _stopping = true;
        if (_dispatcher is { HasShutdownStarted: false } dispatcher)
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        if (_thread != null && !_thread.Join(TimeSpan.FromSeconds(1))) return;
        _thread = null;
        _dispatcher = null;
        _upPressed = _downPressed = false;
    }

    public void Dispose() { Volatile.Write(ref _bindings, []); Stop(); }

    delegate nint KeyboardHook(int code, nint message, nint data);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    static extern nint SetWindowsHookEx(int type, KeyboardHook callback, nint module, uint thread);
    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    static extern nint GetModuleHandle(string? module);
}
