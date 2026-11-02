using System.Windows.Input;
using Spezi.Core;

namespace Spezi.App;

static class HotkeyText
{
    public static string Format(HotkeyBinding binding)
    {
        if (!binding.IsValid)
            return "Not set";
        var parts = new List<string>();
        if ((binding.Modifiers & HotkeyModifiers.Control) != 0)
            parts.Add("Ctrl");
        if ((binding.Modifiers & HotkeyModifiers.Alt) != 0)
            parts.Add("Alt");
        if ((binding.Modifiers & HotkeyModifiers.Shift) != 0)
            parts.Add("Shift");
        if ((binding.Modifiers & HotkeyModifiers.Win) != 0)
            parts.Add("Win");
        parts.Add(binding.VirtualKey switch
        {
            0x21 => "Page Up",
            0x22 => "Page Down",
            _ => KeyInterop.KeyFromVirtualKey(binding.VirtualKey).ToString()
        });
        return string.Join(" + ", parts);
    }

    public static bool TryFromKey(System.Windows.Input.KeyEventArgs args, out HotkeyBinding binding)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        binding = new HotkeyBinding();
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return false;
        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
            modifiers |= HotkeyModifiers.Win;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
            return false;
        binding = new HotkeyBinding(modifiers, virtualKey);
        return true;
    }
}
