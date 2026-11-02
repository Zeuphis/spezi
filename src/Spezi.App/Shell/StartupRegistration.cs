using Microsoft.Win32;

namespace Spezi.App;

static class StartupRegistration
{
    const string ValueName = "Spezi";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null)
            return;
        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return;
        key.SetValue(ValueName, $"\"{exe}\" --background");
    }
}
