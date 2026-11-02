using System.Diagnostics;
using System.Windows;
using Spezi.Core;

namespace Spezi.App;

static partial class SelfTest
{
    // Idle metrics with isolated settings: threads, handles, working set and CPU per UI state.
    public static int RunIdleMetrics(string output)
    {
        var lines = new List<string>();
        var library = Path.Combine(Path.GetTempPath(), "spezi-idle-library"); Directory.CreateDirectory(library);
        using var controller = new AppController(new AppSettings { OutputDirectory = library, AutoReplayWhenPlaying = false, StatusToasts = false }, false, false);
        var window = new MainWindow(controller); window.Show();
        void Click(string name) => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        void Measure(string label, int seconds)
        {
            PumpUntil(() => false, TimeSpan.FromSeconds(3));
            var p = Process.GetCurrentProcess();
            var cpu0 = p.TotalProcessorTime; var t0 = Stopwatch.StartNew();
            PumpUntil(() => false, TimeSpan.FromSeconds(seconds));
            p.Refresh();
            var cpu = (p.TotalProcessorTime - cpu0).TotalMilliseconds / t0.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            ThreadPool.GetAvailableThreads(out _, out _);
            lines.Add($"{label,-22} threads={p.Threads.Count,4} pool={ThreadPool.ThreadCount,3} handles={p.HandleCount,5} ws={p.WorkingSet64 / 1048576,4}MB private={p.PrivateMemorySize64 / 1048576,4}MB cpu={cpu:0.00}%");
        }
        try
        {
            Click("CaptureNav"); Measure("recording-tab", 15);
            if (Environment.GetEnvironmentVariable("SPEZI_IDLE_BREAKDOWN") == "1")
            {
                void Step(string n, Action a) { var p0 = Process.GetCurrentProcess(); int t = p0.Threads.Count, h = p0.HandleCount; a(); PumpUntil(() => false, TimeSpan.FromSeconds(2)); var p1 = Process.GetCurrentProcess(); lines.Add($"step {n,-18} threads {t}->{p1.Threads.Count} handles {h}->{p1.HandleCount}"); }
                Step("audio-devices", () => controller.AudioDevices());
                Step("audio-apps", () => controller.AudioApplications());
                Step("encoders", () => controller.Encoders());
                Step("software-enc", () => controller.SoftwareAvailable());
                Step("free-bytes", () => controller.FreeBytes());
            }
            Click("SettingsNav"); Measure("settings-tab", 15);
            window.WindowState = WindowState.Minimized; Measure("settings-minimized", 15);
            window.WindowState = WindowState.Normal; window.Hide(); Measure("hidden-tray", 15);
            GC.Collect(); GC.WaitForPendingFinalizers(); Measure("hidden-tray-after-gc", 10);
            lines.Add("IDLE_METRICS_OK");
            return 0;
        }
        catch (Exception ex) { lines.Add("FAIL " + ex); return 1; }
        finally { File.WriteAllLines(output, lines); }
    }
}
