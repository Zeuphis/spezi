using System.IO;
using Spezi.Core;
using Spezi.Core.Recording;

namespace Spezi.App;

// Optional automatic cleanup: moves clips older than N days to the Recycle Bin.
// Runs off the UI thread at startup and at most once per day afterwards (checked when a clip is saved);
// no timer of its own. Favorites are never deleted.
static class ClipCleanup
{
    static readonly string StampPath = Path.Combine(Path.GetDirectoryName(AppLog.FilePath)!, "cleanup-last-run.txt");
    static int _running;

    public static void RunIfDue(AppSettings settings, bool force = false)
    {
        if (!settings.AutoDeleteOldClips) return;
        var days = AppSettings.ValidCleanupDays(settings.DeleteClipsOlderThanDays);
        if (!force && DateTime.UtcNow - LastRun() < TimeSpan.FromDays(1)) return;
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        var root = settings.OutputDirectory;
        _ = Task.Run(() =>
        {
            try { Run(root, days); }
            catch (Exception ex) { AppLog.Error("Automatic clip cleanup failed.", ex); }
            finally { Volatile.Write(ref _running, 0); }
        });
    }

    static DateTime LastRun()
    {
        try
        {
            return File.Exists(StampPath) && long.TryParse(File.ReadAllText(StampPath), out var ticks)
                ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
        }
        catch (IOException) { return DateTime.MinValue; }
    }

    static void Run(string root, int days)
    {
        File.WriteAllText(StampPath, DateTime.UtcNow.Ticks.ToString());
        if (!Directory.Exists(root)) return;
        var cutoff = DateTime.Now.AddDays(-days);
        int deleted = 0, kept = 0;
        foreach (var path in ClipLibrary.VideoFiles(root).ToList())
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.LastWriteTime >= cutoff) continue;
            if (Favorites.Contains(root, path)) { kept++; continue; }
            var thumbnail = ClipCache.FileFor(path);
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                if (File.Exists(path)) continue;
                deleted++;
                AppLog.Info($"Cleanup moved to Recycle Bin: {path} ({file.LastWriteTime:yyyy-MM-dd})");
                try { File.Delete(thumbnail); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                AppLog.Error("Cleanup could not delete " + path, ex);
            }
        }
        AppLog.Info($"Cleanup finished: {deleted} clip(s) older than {days} days moved to Recycle Bin, {kept} favorite(s) kept.");
    }
}
