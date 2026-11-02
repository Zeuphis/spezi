using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Spezi.Core;

namespace Spezi.App;

static class VideoLauncher
{
    public static void Open(string path, Window? owner)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                ShowError(owner, "This video was moved or deleted and is no longer at its saved location.");
                return;
            }

            using var player = Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true, Verb = "open" });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1155)
        {
            AppLog.Error("No default app for video: " + path, ex);
            ShowError(owner, "Choose an app for .mp4 files in Windows Settings → Apps → Default apps.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppLog.Error("Could not open video in the default app: " + path, ex);
            ShowError(owner, "The video could not be opened. Check that the file opens in File Explorer and that a default app is set for .mp4 files.");
        }
    }

    static void ShowError(Window? owner, string message)
    {
        if (owner != null)
            System.Windows.MessageBox.Show(owner, message, "Spezi", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            System.Windows.MessageBox.Show(message, "Spezi", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
