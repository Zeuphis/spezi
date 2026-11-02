using System.Globalization;
using System.Threading;
using System.Windows.Markup;
using System.Windows;

namespace Spezi.App;

public partial class App : System.Windows.Application
{
    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    AppController? _controller;
    MainWindow? _window;

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
        DispatcherUnhandledException += (_, args) =>
        {
            Spezi.Core.AppLog.Error("The window reported an error.", args.Exception);
            System.Windows.MessageBox.Show(
                "Spezi encountered an error. Details are in %LocalAppData%\\Spezi\\spezi.log.",
                "Spezi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
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
        var waiter = new Thread(WaitForShow) { IsBackground = true, Name = "Spezi show" };
        waiter.Start();

        _controller = new AppController();
        _window = new MainWindow(_controller);
        _window.Show();
        if (e.Args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            _window.Hide();

        _controller.Recover();
        _window.CheckForUpdatesOnStartup();
    }

    void WaitForShow()
    {
        while (_showEvent != null)
        {
            if (!_showEvent.WaitOne(TimeSpan.FromSeconds(2)))
                continue;
            Dispatcher.BeginInvoke(() => _window?.BringForward());
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _showEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
