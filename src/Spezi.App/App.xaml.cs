using System.Globalization;
using System.Threading;
using System.Windows.Markup;
using System.Windows;

namespace Spezi.App;

public partial class App : System.Windows.Application
{
    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    RegisteredWaitHandle? _showWait;
    AppController? _controller;
    MainWindow? _window;

    static bool _errorDialogOpen;

    // Crash reports in %LocalAppData%\Spezi\logs; keeps the newest 20.
    internal static void WriteCrashLog(string title, Exception? exception)
    {
        try
        {
            var folder = Path.Combine(Path.GetDirectoryName(Spezi.Core.AppLog.FilePath)!, "logs");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
            var process = System.Diagnostics.Process.GetCurrentProcess();
            File.WriteAllText(file,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {title}{Environment.NewLine}" +
                $"Version: {typeof(App).Assembly.GetName().Version}{Environment.NewLine}" +
                $"OS: {Environment.OSVersion}{Environment.NewLine}" +
                $"Uptime: {DateTime.Now - process.StartTime}{Environment.NewLine}" +
                $"Memory: working set {process.WorkingSet64 / 1048576} MB, private {process.PrivateMemorySize64 / 1048576} MB, handles {process.HandleCount}, threads {process.Threads.Count}{Environment.NewLine}" +
                $"Thread: {Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString()}{Environment.NewLine}{Environment.NewLine}" +
                exception);
            foreach (var old in new DirectoryInfo(folder).GetFiles("crash-*.log").OrderByDescending(f => f.Name).Skip(20))
                old.Delete();
        }
        catch
        {
            // Crash logging must never throw.
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("en-US")));
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("Unhandled exception (app terminating: " + args.IsTerminating + ")", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Spezi.Core.AppLog.Error("Unobserved background task error.", args.Exception);
            args.SetObserved();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            Spezi.Core.AppLog.Error("The window reported an error.", args.Exception);
            WriteCrashLog("UI error (handled)", args.Exception);
            args.Handled = true;
            // One dialog at a time: a repeating error must not stack modal boxes and freeze the app.
            if (_errorDialogOpen) return;
            _errorDialogOpen = true;
            try
            {
            System.Windows.MessageBox.Show(
                "Spezi encountered an error. Details are in %LocalAppData%\\Spezi\\spezi.log.",
                "Spezi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            }
            finally { _errorDialogOpen = false; }
        };

#if SPEZI_SELFTESTS
        // Self-test entry points; compiled only with -p:SpeziSelfTests=true (default in Debug).
        if (e.Args.Any(a => a.Equals("--background-replay-test", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SelfTest.RunBackgroundReplay());
            return;
        }

        if (e.Args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            var code = SelfTest.Run();
            Shutdown(code);
            return;
        }

        if (e.Args.Any(a => a.Equals("--hotkey-test", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SelfTest.RunHotkeys());
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0].Equals("--media-test", StringComparison.OrdinalIgnoreCase))
        {
            var code = SelfTest.RunMedia(e.Args[1], e.Args.Length > 2 ? e.Args[2] : null);
            Shutdown(code);
            return;
        }

        if (e.Args.Any(a => a.Equals("--settings-test", StringComparison.OrdinalIgnoreCase)))
        {
            var code = SelfTest.RunSettings();
            Shutdown(code);
            return;
        }

        if (e.Args.Any(a => a.Equals("--replay-audio-settings-test", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SelfTest.RunReplayAudioSettings());
            return;
        }

        if (e.Args.Any(a => a.Equals("--toast-test", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SelfTest.RunToasts());
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0].Equals("--idle-metrics", StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest.RunIdleMetrics(e.Args[1]));
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0].Equals("--ui-screenshots", StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest.RunUiScreenshots(e.Args[1]));
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0].Equals("--gallery-design-test", StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest.RunGalleryDesign(e.Args[1]));
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0].Equals("--gallery-performance-test", StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest.RunGalleryPerformance(e.Args[1]));
            return;
        }

#endif
        if (e.Args.Length == 1 && e.Args[0] == AppController.DetectEncodersArgument)
        {
            // Child process of AppController.Encoders(): print and exit, no window, no mutex.
            try
            {
                using var recorder = new Spezi.Core.Recorder();
                using var stdout = new StreamWriter(Console.OpenStandardOutput());
                stdout.Write(AppController.FormatEncoderLines(recorder.DetectEncoders()));
                Shutdown(0);
            }
            catch (Exception ex) { Spezi.Core.AppLog.Error("Encoder detection failed.", ex); Shutdown(1); }
            return;
        }

        _mutex = new Mutex(true, @"Local\Spezi.SingleInstance", out var created);
        if (!created)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(@"Local\Spezi.Show");
                existing.Set();
            }
            catch
            {
                // The first instance will show itself the next time it starts.
            }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Spezi.Show");
        // Thread-pool wait: no dedicated thread and no periodic wake-ups.
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _window?.BringForward()), null, Timeout.Infinite, false);

        _controller = new AppController();
        _window = new MainWindow(_controller);
        _window.Show();
        if (e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            _window.Hide();
        if (!_window.IsVisible) _window.TrimWhenIdle();

        _controller.Recover();
        _controller.StartAutoReplay();
        ClipCleanup.RunIfDue(_controller.Settings, force: true);
        _window.CheckForUpdatesOnStartup();
    }


    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
