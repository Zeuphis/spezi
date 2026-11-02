using System.Runtime.InteropServices;
using System.Windows.Interop;
using Spezi.Core;

namespace Spezi.App;

sealed class HotkeyHost : IDisposable
{
    const int WmHotkey = 0x0312;
    readonly HwndSource _source;
    PageKeyFallback? _pageKeys;
    bool _disposed;

    public event Action<int>? Pressed;

    public HotkeyHost()
    {
        var parameters = new HwndSourceParameters("SpeziHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(Hook);
    }

    public bool TryRegister(int id, HotkeyBinding binding, out string error)
    {
        Unregister(id);
        if (!binding.IsValid)
        {
            error = "The hotkey is incomplete.";
            return false;
        }

        var modifiers = binding.Modifiers | HotkeyModifiers.NoRepeat;
        if (RegisterHotKey(_source.Handle, id, modifiers, (uint)binding.VirtualKey))
        {
            error = "";
            return true;
        }

        var code = Marshal.GetLastWin32Error();
        if (code == 1409 && binding.VirtualKey is 0x21 or 0x22)
        {
            if (_pageKeys == null)
            {
                _pageKeys = new PageKeyFallback();
                _pageKeys.Pressed += (pressedId, token) => _source.Dispatcher.BeginInvoke(() =>
                {
                    if (!_disposed && _pageKeys.IsCurrent(pressedId, token)) Pressed?.Invoke(pressedId);
                });
            }
            if (_pageKeys.TryRegister(id, binding, out code))
            {
                AppLog.Info($"Page-key fallback active: id={id}, key={binding.VirtualKey}, modifiers={binding.Modifiers}");
                error = "";
                return true;
            }
        }
        AppLog.Info($"Hotkey registration failed: id={id}, key={binding.VirtualKey}, modifiers={binding.Modifiers}, error={code}");
        error = code == 1409
            ? "This shortcut is already in use by Windows or another app."
            : $"Windows could not register the hotkey (error {code}).";
        return false;
    }

    public void Unregister(int id)
    {
        UnregisterHotKey(_source.Handle, id);
        _pageKeys?.Unregister(id);
    }

    nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            Pressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        _disposed = true;
        Unregister(1);
        Unregister(2);
        _pageKeys?.Dispose();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(nint hWnd, int id);
}
