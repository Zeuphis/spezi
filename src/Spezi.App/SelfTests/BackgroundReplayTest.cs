using Spezi.Core;
using Spezi.Core.Recording;

namespace Spezi.App;

static partial class SelfTest
{
    public static int RunBackgroundReplay()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpeziBackgroundReplayTest", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var original = File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null;
        var report = new List<string>();
        void Check(bool value, string text) { if (!value) throw new Exception(text); report.Add("PASS " + text); }
        try
        {
            using var controller = new AppController(new AppSettings
            {
                SetupCompleted = true, OutputDirectory = directory, AutoReplayWhenPlaying = true, StatusToasts = false,
                Width = 1280, Height = 720, FrameRate = 30, ReplaySeconds = 30,
                AllowSoftwareEncoder = false, CaptureCursor = false
            }, false, false);
            var starts = 0;
            var stops = 0;
            string? saved = null;
            controller.Notice += notice =>
            {
                if (notice.Kind == NoticeKind.ReplayStarted) starts++;
                if (notice.Kind == NoticeKind.ReplayStopped) stops++;
                if (notice.Kind == NoticeKind.ClipSaved) saved = notice.FilePath;
            };
            controller.SelectMonitor(controller.Displays().First(d => d.IsPrimary));
            controller.WatchGame(null);
            Check(!controller.Snapshot().ReplayActive, "Game detection alone never starts replay");
            controller.StartAutoReplay();
            Thread.Sleep(1600);
            var before = controller.Snapshot();
            Check(before.ReplayActive && before.ReplayBuffered.TotalSeconds > 0.4, "Automatic replay starts at launch without a game");
            controller.WatchGame("Overwatch");
            // Poll the actual controller with no foreground game for longer than
            // the former four-poll timeout. Capture and encoding remain real.
            for (var i = 0; i < 5; i++)
            {
                controller.WatchGame(null);
                Thread.Sleep(2100);
            }
            var desktop = controller.Snapshot();
            Check(desktop.ReplayActive && desktop.PipelineRunning && desktop.ReplayBuffered.TotalSeconds > 10,
                "Desktop capture continues beyond eight seconds and retains the original replay buffer");
            controller.WatchGame("Overwatch");
            controller.WatchGame(null);
            Check(starts == 1 && stops == 0, "Returning to a game and tabbing away again never restarts or stops replay");
            var save = controller.SaveClip();
            PumpUntil(() => save.IsCompleted, TimeSpan.FromSeconds(10));
            Check(save.IsCompleted, "Saving the desktop replay finishes without blocking the UI");
            save.GetAwaiter().GetResult();
            Check(saved != null && File.Exists(saved), "A clip can be saved while no game is in the foreground");
            var media = MediaInspector.Probe(saved!);
            Check(media.HasVideo && media.HasAudio && media.Duration.TotalSeconds > 9,
                "Saved desktop replay contains playable video and audio covering the complete interval");
            controller.Settings.AutoReplayWhenPlaying = false;
            controller.Save();
            controller.WatchGame(null);
            Check(controller.Snapshot().ReplayActive, "Disabling automatic starts does not discard an active replay");
            controller.StopReplay();
            Check(!controller.Snapshot().ReplayActive && !controller.Snapshot().PipelineRunning && stops == 1,
                "Explicitly turning replay off still stops screen capture");
            controller.StartAutoReplay();
            Check(!controller.Snapshot().ReplayActive, "Disabled auto-start leaves replay off at launch");
            controller.StartReplay();
            controller.WatchGame(null);
            Check(controller.Snapshot().ReplayActive, "Manually enabled replay also remains active on the desktop");
            controller.StopReplay();
            Check((File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null) == original,
                "Real user settings remain unchanged");
            report.Add("CLIP=" + saved);
            report.Add("BACKGROUND_REPLAY_TEST_OK");
            return 0;
        }
        catch (Exception ex) { report.Add("BACKGROUND_REPLAY_TEST_FAILED " + ex); return 1; }
        finally { File.WriteAllLines(Path.Combine(directory, "result.txt"), report); }
    }
}
