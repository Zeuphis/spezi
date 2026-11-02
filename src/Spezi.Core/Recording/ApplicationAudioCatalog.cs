using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NAudio.CoreAudioApi;

namespace Spezi.Core.Recording;

internal static class ApplicationAudioCatalog
{
    static readonly string[] DiscordNames = ["Discord", "DiscordCanary", "DiscordPTB"];
    static readonly string[] TeamSpeakNames = ["TeamSpeak", "ts3client_win64", "ts3client_win32"];

    public static IReadOnlyList<AudioApplicationEntry> ListApplications(IEnumerable<string>? selected)
    {
        var processes = Snapshot();
        var audible = new HashSet<int>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    try
                    {
                        var sessions = device.AudioSessionManager.Sessions;
                        for (var i = 0; i < sessions.Count; i++)
                        {
                            using var session = sessions[i];
                            if (!session.IsSystemSoundsSession) audible.Add((int)session.GetProcessID);
                        }
                    }
                    catch (Exception ex) { AppLog.Info("Audio application discovery skipped endpoint: " + ex.Message); }
                }
            }
        }
        catch (Exception ex) { AppLog.Info("Audio application discovery unavailable: " + ex.Message); }
        var names = processes.Where(p => audible.Contains(p.Id)).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Also offer silent windowed apps so users can choose a game before its audio starts.
        foreach (var window in CaptureSources.ListWindows()) names.Add(window.ProcessName);
        foreach (var id in selected ?? []) if (id.StartsWith("exe:", StringComparison.OrdinalIgnoreCase)) names.Add(id[4..]);
        names.ExceptWith(DiscordNames.Concat(TeamSpeakNames).Concat(["Spezi", "Spezi.App", "explorer", "System", "Idle"]));
        var list = new List<AudioApplicationEntry>
        {
            new() { Id = AudioApplicationEntry.GameId, Name = "Current game", IsRunning = false },
            new() { Id = AudioApplicationEntry.DiscordId, Name = "Discord", IsRunning = processes.Any(p => DiscordNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) },
            new() { Id = AudioApplicationEntry.TeamSpeakId, Name = "TeamSpeak", IsRunning = processes.Any(p => TeamSpeakNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) }
        };
        list.AddRange(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).Select(name => new AudioApplicationEntry
        {
            Id = "exe:" + name.ToLowerInvariant(), Name = name,
            IsRunning = processes.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        }));
        return list;
    }

    internal static IReadOnlyList<ProcessEntry> Snapshot()
    {
        using var handle = CreateToolhelp32Snapshot(2, 0);
        if (handle.IsInvalid) return [];
        var native = new NativeProcessEntry { Size = (uint)Marshal.SizeOf<NativeProcessEntry>() };
        var entries = new List<ProcessEntry>();
        if (Process32First(handle, ref native))
            do
            {
                entries.Add(new((int)native.ProcessId, (int)native.ParentId, Path.GetFileNameWithoutExtension(native.Executable)));
                native.Size = (uint)Marshal.SizeOf<NativeProcessEntry>();
            } while (Process32Next(handle, ref native));
        return entries;
    }

    internal static IReadOnlyList<AudioProcessTarget> ResolveTargets(IEnumerable<string> selection, IReadOnlyList<ProcessEntry> processes, int gameId)
    {
        var selected = selection.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<int, (ProcessEntry Process, string SourceId, string Name)>();
        foreach (var process in processes)
        {
            if (process.Id <= 0 || process.Id == Environment.ProcessId) continue;
            var key = "exe:" + process.Name.ToLowerInvariant();
            if (selected.Contains(key)) candidates[process.Id] = (process, key, process.Name);
            if (selected.Contains(AudioApplicationEntry.DiscordId) && DiscordNames.Contains(process.Name, StringComparer.OrdinalIgnoreCase))
                candidates[process.Id] = (process, AudioApplicationEntry.DiscordId, "Discord");
            if (selected.Contains(AudioApplicationEntry.TeamSpeakId) && TeamSpeakNames.Contains(process.Name, StringComparer.OrdinalIgnoreCase))
                candidates[process.Id] = (process, AudioApplicationEntry.TeamSpeakId, "TeamSpeak");
            if (selected.Contains(AudioApplicationEntry.GameId) && process.Id == gameId)
                candidates[process.Id] = (process, AudioApplicationEntry.GameId, "Current game");
        }
        var byId = processes.ToDictionary(p => p.Id);
        var targets = new List<AudioProcessTarget>();
        foreach (var candidate in candidates.Values)
        {
            // Capture a tree once even when its root and audio subprocess are both selected.
            var parent = candidate.Process.ParentId;
            var visited = new HashSet<int> { candidate.Process.Id };
            var covered = false;
            while (parent > 0 && visited.Add(parent) && byId.TryGetValue(parent, out var ancestor))
            {
                if (candidates.ContainsKey(parent)) { covered = true; break; }
                parent = ancestor.ParentId;
            }
            if (covered) continue;
            try
            {
                using var process = Process.GetProcessById(candidate.Process.Id);
                if (!process.HasExited && process.ProcessName.Equals(candidate.Process.Name, StringComparison.OrdinalIgnoreCase))
                    targets.Add(new(candidate.Process.Id, process.StartTime.ToUniversalTime().Ticks, candidate.SourceId, candidate.Name));
            }
            catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        return targets;
    }

    internal record ProcessEntry(int Id, int ParentId, string Name);
    internal record AudioProcessTarget(int Id, long Started, string SourceId, string Name)
    {
        public string SlotId => $"app:{Id}:{Started}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NativeProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public nuint DefaultHeap;
        public uint ModuleId, ThreadCount, ParentId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool Process32First(SafeFileHandle handle, ref NativeProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool Process32Next(SafeFileHandle handle, ref NativeProcessEntry entry);
}
