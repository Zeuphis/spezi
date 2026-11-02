using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Spezi.Core.Recording;

internal static class SessionRecovery
{
    sealed record RecoveryState(string Fingerprint, string Status, string? Output);

    public static IReadOnlyList<string> Recover(string root, string outputDirectory)
    {
        if (!Directory.Exists(root)) return [];
        var recovered = new List<string>();
        foreach (var directory in Directory.GetDirectories(root))
        {
            var parts = Directory.GetFiles(directory, "part-*.mp4").OrderBy(p => p).ToArray();
            if (parts.Length == 0) continue;
            var identity = string.Join('\n', parts.Select(p => { var f = new FileInfo(p); return $"{f.Name}|{f.Length}|{f.LastWriteTimeUtc.Ticks}"; }));
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            var statePath = Path.Combine(directory, "recovery-state.json");
            try
            {
                if (File.Exists(statePath))
                {
                    var state = JsonSerializer.Deserialize<RecoveryState>(File.ReadAllText(statePath));
                    if (state?.Fingerprint == fingerprint)
                    {
                        if (state.Status is "Recovered" or "NoPlayableParts") continue;
                        if (state.Status == "Publishing" && state.Output != null && IsPlayable(state.Output))
                        {
                            SaveState(statePath, state with { Status = "Recovered" });
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex) { AppLog.Error("Recovery state could not be read.", ex); }

            string? pending = null;
            try
            {
                var valid = parts.Where(IsPlayable).ToArray();
                if (valid.Length == 0)
                {
                    SaveState(statePath, new(fingerprint, "NoPlayableParts", null));
                    AppLog.Info("Recovery retained unplayable original parts: " + directory);
                    continue;
                }
                var titlePath = Path.Combine(directory, "game-title.txt");
                var title = File.Exists(titlePath) ? File.ReadAllText(titlePath).Trim() : null;
                var gameDirectory = ClipLibrary.GameDirectory(outputDirectory, title);
                Directory.CreateDirectory(gameDirectory);
                var name = string.IsNullOrWhiteSpace(title)
                    ? $"Spezi_Recovered_{Path.GetFileName(directory)}.mp4"
                    : RecordingNames.Create("Recovered", title);
                var legacyName = $"Spezi_Wiederhergestellt_{Path.GetFileName(directory)}.mp4";
                if (string.IsNullOrWhiteSpace(title) && File.Exists(Path.Combine(gameDirectory, legacyName)))
                    name = legacyName;
                var output = Path.Combine(gameDirectory, name);
                // Do not replace a valid video, even if a previous attempt used its name.
                if (File.Exists(output) && IsPlayable(output))
                    output = Path.Combine(gameDirectory, RecordingNames.Create("Recovered", title));
                var pendingRoot = Path.Combine(gameDirectory, ".spezi-pending");
                Directory.CreateDirectory(pendingRoot);
                pending = Path.Combine(pendingRoot, Guid.NewGuid().ToString("N") + ".mp4");
                SegmentedSession.Remux(valid, pending);
                if (!IsPlayable(pending)) throw new InvalidOperationException("The recovered recording contains no playable video.");
                if (File.Exists(output))
                {
                    var backup = Path.Combine(pendingRoot, "previous-recovery");
                    Directory.CreateDirectory(backup);
                    File.Copy(output, Path.Combine(backup, Path.GetFileName(output) + "." + Guid.NewGuid().ToString("N") + ".bak"));
                }
                // Persist the chosen destination before publication so a crash between
                // the atomic move and the completion marker cannot create a duplicate.
                SaveState(statePath, new(fingerprint, "Publishing", output));
                File.Move(pending, output, true);
                pending = null;
                SaveState(statePath, new(fingerprint, "Recovered", output));
                recovered.Add(output);
                AppLog.Info($"Recovered {valid.Length} playable parts; retained {parts.Length - valid.Length} damaged parts: {directory}");
                // Keep the originals and their marker. An unchanged session is handled once.
            }
            catch (Exception ex) { AppLog.Error("Could not recover " + directory, ex); }
            finally
            {
                if (pending != null)
                {
                    try { File.Delete(pending); } catch { /* private incomplete output remains hidden */ }
                }
            }
        }
        return recovered;
    }

    static bool IsPlayable(string path)
    {
        try
        {
            if (new FileInfo(path).Length < 128) return false;
            var media = MediaInspector.Probe(path);
            return media.HasVideo && media.Duration > TimeSpan.Zero;
        }
        catch { return false; }
    }

    static void SaveState(string path, RecoveryState state)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, path, true);
    }
}
