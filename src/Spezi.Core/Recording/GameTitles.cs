using System.Diagnostics;

namespace Spezi.Core.Recording;

public static class GameTitles
{
    static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Overwatch"] = "Overwatch", ["Overwatch2"] = "Overwatch 2",
        ["RainbowSix"] = "Rainbow Six Siege", ["RainbowSix_Vulkan"] = "Rainbow Six Siege",
        ["RainbowSix_DX11"] = "Rainbow Six Siege", ["RainbowSix_DX12"] = "Rainbow Six Siege",
        ["VALORANT-Win64-Shipping"] = "VALORANT", ["FortniteClient-Win64-Shipping"] = "Fortnite",
        ["cs2"] = "Counter-Strike 2", ["r5apex"] = "Apex Legends", ["RocketLeague"] = "Rocket League"
    };
    static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "Spezi", "explorer", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost",
        "ApplicationFrameHost", "TextInputHost", "SystemSettings", "LockApp", "Idle", "dwm",
        "sihost", "ctfmon", "chrome", "msedge", "firefox", "opera", "brave", "Code", "devenv",
        "notepad", "Discord", "DiscordCanary", "DiscordPTB", "Teams", "ms-teams", "TeamSpeak", "ts3client_win64", "ts3client_win32", "steam", "steamwebhelper",
        "ChatGPT", "Codex", "vlc", "wmplayer", "mpv", "Microsoft.Media.Player", "Video.UI",
        "Battle.net", "UbisoftConnect", "EpicGamesLauncher"
    };

    public static string? Detect(CaptureSelection capture)
    {
        var window = capture.Kind == CaptureKind.Window ? capture.Handle : Native.GetForegroundWindow();
        return DetectWindow(window);
    }

    internal static int DetectProcessId(CaptureSelection? capture)
    {
        var window = capture?.Kind == CaptureKind.Window ? capture.Handle : Native.GetForegroundWindow();
        if (DetectWindow(window) == null) return 0;
        Native.GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }

    static string? DetectWindow(nint window)
    {
        if (window == 0 || !Native.IsWindowVisible(window)) return null;
        Native.GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var name = process.ProcessName;
            if (Ignored.Contains(name)) return null;
            if (Known.TryGetValue(name, out var known)) return known;
            var text = new char[Math.Min(512, Native.GetWindowTextLength(window) + 1)];
            Native.GetWindowText(window, text, text.Length);
            var title = new string(text).TrimEnd('\0').Trim();
            var fullscreen = false;
            if (Native.GetWindowRect(window, out var rect))
                foreach (var display in CaptureSources.ListDisplays())
                {
                    var info = new Native.MonitorInfoEx { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfoEx>() };
                    if (!Native.GetMonitorInfo(display.MonitorHandle, ref info)) continue;
                    var overlap = (long)Math.Max(0, Math.Min(rect.Right, info.Monitor.Right) - Math.Max(rect.Left, info.Monitor.Left)) *
                        Math.Max(0, Math.Min(rect.Bottom, info.Monitor.Bottom) - Math.Max(rect.Top, info.Monitor.Top));
                    if (overlap >= (long)info.Monitor.Width * info.Monitor.Height * 0.85) { fullscreen = true; break; }
                }
            return Resolve(name, title, fullscreen);
        }
        catch { return null; }
    }

    internal static string? Resolve(string process, string title, bool fullscreen)
    {
        if (Ignored.Contains(process)) return null;
        if (Known.TryGetValue(process, out var known)) return known;
        if (!fullscreen) return null;
        return string.IsNullOrWhiteSpace(title) ? process : title;
    }
}

internal static class RecordingNames
{
    public static string Create(string kind, string? gameTitle)
    {
        var title = string.IsNullOrWhiteSpace(gameTitle) ? "Desktop" : Clean(gameTitle);
        return $"{title}_{kind}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid().ToString("N")[..12]}.mp4";
    }

    internal static string Clean(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(title.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (safe.Length > 70) safe = safe[..70].Trim(' ', '.');
        var stem = safe.Split('.')[0];
        if (System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            safe = "_" + safe;
        return safe.Length == 0 ? "Spezi" : safe;
    }
}
