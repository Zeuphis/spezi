using System.Diagnostics;
using Spezi.Core;
using Spezi.Core.Recording;

namespace Spezi.App;

static partial class SelfTest
{
    public static int RunReplayAudioSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpeziReplayAudioSettingsTest", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        var originalSettings = File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null;
        System.Windows.Window? window = null;
        void Check(bool value, string message) { if (!value) throw new Exception(message); report.Add("PASS " + message); }
        void Capture(string name, System.Windows.FrameworkElement visual)
        {
            visual.UpdateLayout();
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var drawing = new System.Windows.Media.DrawingVisual();
            using (var context = drawing.RenderOpen()) context.DrawRectangle(new System.Windows.Media.VisualBrush(visual), null, new System.Windows.Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
            image.Render(drawing);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
        }
        try
        {
            using var controller = new AppController(new AppSettings { SetupCompleted = true, OutputDirectory = directory, AutoReplayWhenPlaying = false, StatusToasts = false }, false, false);
            var capture = new CapturePage(controller);
            window = new System.Windows.Window { Content = capture, Width = 460, Height = 760, ShowInTaskbar = false, Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("Bg") };
            window.Show(); capture.RefreshStatus();
            PumpUntil(() => false, TimeSpan.FromMilliseconds(120));
            Check(capture.FindName("ReplayDuration") == null && controller.Settings.ReplaySeconds == 180,
                "Recording has no replay-length slider and preserves the configured duration");
            Capture("recording-only.png", window);
            var settings = new SettingsPage(controller); window.Content = settings; settings.Reload();
            PumpUntil(() => false, TimeSpan.FromMilliseconds(120));
            var duration = (ReplayDurationControl)settings.FindName("ReplayDuration");
            duration.BringIntoView(); PumpUntil(() => false, TimeSpan.FromMilliseconds(80));
            var slider = (System.Windows.Controls.Slider)duration.FindName("DurationSlider");
            foreach (var seconds in Enumerable.Range(1, 20).Select(n => n * 15))
            {
                slider.Value = seconds;
                Check(controller.Settings.ReplaySeconds == seconds, $"settings slider saves {seconds} seconds");
            }
            slider.Value = 79;
            Check(controller.Settings.ReplaySeconds == 75 && slider.Value == 75, "slider and saved value agree after snapping to a 15-second step");
            var marks = (System.Windows.Controls.Canvas)duration.FindName("Marks");
            var dots = marks.Children.OfType<System.Windows.Shapes.Ellipse>().ToArray();
            Check(dots.Length == 20 && dots.Count(dot => dot.Width == 6) == 5 && dots.Where(dot => dot.Width == 6).All(dot => (int)dot.Tag % 60 == 0), "20 step dots and five larger full-minute dots are rendered");
            Check(((System.Windows.Controls.TextBlock)duration.FindName("DurationText")).Text == "1m 15s", "exact replay duration is displayed");
            Capture("settings-slider-mint.png", window);
            controller.Settings.AccentColor = "#AC8BFA"; controller.Save();
            slider.ApplyTemplate();
            var track = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
            track.Thumb.ApplyTemplate();
            var thumbDot = (System.Windows.Shapes.Ellipse)System.Windows.Media.VisualTreeHelper.GetChild(track.Thumb, 0);
            Check(((System.Windows.Media.SolidColorBrush)thumbDot.Fill).Color == AppTheme.Parse("#AC8BFA"), "selected point adopts the current app accent immediately");
            Capture("settings-slider-violet.png", window);
            settings.Reload();
            PumpUntil(() => false, TimeSpan.FromMilliseconds(120));
            var settingsDuration = (ReplayDurationControl)settings.FindName("ReplayDuration");
            Check(settingsDuration.Seconds == 75, "settings slider loads the same replay duration");
            settingsDuration.Seconds = 45;
            Check(controller.Settings.ReplaySeconds == 45, "settings slider also saves sub-minute durations");
            var mode = (System.Windows.Controls.ComboBox)settings.FindName("AudioModeBox");
            mode.SelectedIndex = 1;
            Check(controller.Settings.AudioMode == AudioCaptureMode.Applications, "app-only audio mode is selectable");
            settings.LoadAudioNow();
            var appList = (System.Windows.Controls.StackPanel)settings.FindName("ApplicationAudioList");
            var toggles = appList.Children.OfType<System.Windows.Controls.Border>().Select(b => b.Child).OfType<System.Windows.Controls.Grid>().SelectMany(grid => grid.Children.OfType<System.Windows.Controls.CheckBox>()).ToArray();
            foreach (var toggle in toggles) toggle.IsChecked = toggle.Tag is AudioApplicationEntry app && app.Id is AudioApplicationEntry.DiscordId or AudioApplicationEntry.TeamSpeakId;
            Check(controller.Settings.AudioApplicationIds.Order().SequenceEqual(new[] { AudioApplicationEntry.DiscordId, AudioApplicationEntry.TeamSpeakId }.Order()), "Discord and TeamSpeak can be selected together without the game");
            var scroll = (System.Windows.Controls.ScrollViewer)settings.FindName("SettingsScroll");
            Capture("settings-full.png", (System.Windows.FrameworkElement)scroll.Content);
            var panel = (System.Windows.FrameworkElement)settings.FindName("ApplicationAudioPanel");
            panel.BringIntoView(); PumpUntil(() => false, TimeSpan.FromMilliseconds(80));
            Capture("settings-app-audio.png", window);
            var system = (System.Windows.Controls.CheckBox)settings.FindName("SystemAudioBox"); system.IsChecked = false;
            Check(!mode.IsEnabled && !controller.Settings.CaptureSystemAudio!.Value, "audio master disables app capture while microphone controls remain independent");
            system.IsChecked = true; mode.SelectedIndex = 0;
            Check(controller.Settings.AudioMode == AudioCaptureMode.System && controller.Settings.AudioApplicationIds.Count == 2, "returning to system audio preserves the selected apps");
            window.Close();
            using var firstController = new AppController(new AppSettings
            {
                SetupCompleted = false, OutputDirectory = directory, AutoReplayWhenPlaying = false, StatusToasts = false,
                Profile = QualityProfile.Custom, Width = 1600, Height = 900, FrameRate = 30, ReplaySeconds = 75,
                AudioMode = AudioCaptureMode.Applications, AudioApplicationIds = [AudioApplicationEntry.DiscordId],
                ReplayHotkey = new(0, 0x7C), RecordHotkey = new(0, 0x7D)
            }, false, false);
            var firstWindow = new MainWindow(firstController);
            window = firstWindow;
            firstWindow.Show(); PumpUntil(() => false, TimeSpan.FromMilliseconds(120));
            Check(((System.Windows.Controls.ContentControl)firstWindow.FindName("Body")).Content is CapturePage firstCapture
                && firstCapture.FindName("ReplayDuration") == null,
                "first launch opens Recording without a setup wizard or duration slider");
            Check(firstController.Settings.Profile == QualityProfile.Custom && firstController.Settings.Width == 1600
                && firstController.Settings.FrameRate == 30 && firstController.Settings.AudioApplicationIds.SequenceEqual(new[] { AudioApplicationEntry.DiscordId }),
                "opening the app preserves existing video and audio configuration");
            Check(System.Globalization.CultureInfo.CurrentCulture.Name == "en-US" && HotkeyText.Format(new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x77)) == "Ctrl + Shift + F8",
                "English culture and hotkey labels are used on a German Windows desktop");
            Capture("english-capture.png", firstWindow);
            ((System.Windows.Controls.Button)firstWindow.FindName("SettingsNav")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var firstSettings = (SettingsPage)((System.Windows.Controls.ContentControl)firstWindow.FindName("Body")).Content;
            Check(((System.Windows.Controls.ComboBox)firstSettings.FindName("AudioModeBox")).SelectedIndex == 1,
                "all audio configuration is available in Settings immediately after first launch");
            var firstDuration = (ReplayDurationControl)firstSettings.FindName("ReplayDuration");
            firstDuration.BringIntoView(); PumpUntil(() => false, TimeSpan.FromMilliseconds(80));
            Capture("english-settings-slider.png", firstWindow);
            firstWindow.Width = 400; firstWindow.UpdateLayout();
            var firstMarks = (System.Windows.Controls.Canvas)firstDuration.FindName("Marks");
            var labels = firstMarks.Children.OfType<System.Windows.Controls.TextBlock>().OrderBy(System.Windows.Controls.Canvas.GetLeft).ToArray();
            Check(labels.Zip(labels.Skip(1)).All(pair => System.Windows.Controls.Canvas.GetLeft(pair.First) + pair.First.DesiredSize.Width + 3 <= System.Windows.Controls.Canvas.GetLeft(pair.Second)),
                "scale labels remain separated at the minimum window width");
            Check((File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null) == originalSettings, "real user settings remain unchanged");
            report.Add("REPLAY_AUDIO_SETTINGS_TEST_OK"); return 0;
        }
        catch (Exception ex) { report.Add("REPLAY_AUDIO_SETTINGS_TEST_FAILED " + ex); return 1; }
        finally
        {
            if (window is MainWindow main)
                typeof(MainWindow).GetField("_exiting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(main, true);
            window?.Close(); File.WriteAllLines(Path.Combine(directory, "result.txt"), report);
        }
    }

    public static int RunHotkeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpeziHotkeyTest", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        var originalSettings = File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null;
        MainWindow? window = null;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.Add("PASS " + message);
        }
        try
        {
            Check(HotkeyText.Format(new HotkeyBinding(0, 0x21)) == "Page Up" && HotkeyText.Format(new HotkeyBinding(0, 0x22)) == "Page Down", "clear Page Up / Page Down labels");
            Check(HotkeyText.Format(new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x22)) == "Ctrl + Shift + Page Down", "page key labels retain modifiers");
            using var controller = new AppController(new AppSettings
            {
                SetupCompleted = true, AutoReplayWhenPlaying = false, StatusToasts = false,
                OutputDirectory = directory, ReplayHotkey = new(0, 0x21), RecordHotkey = new(0, 0x22)
            }, false, false);
            window = new MainWindow(controller);
            window.Show();
            window.Activate();
            PumpUntil(() => window.IsActive, TimeSpan.FromSeconds(3));
            Check(controller.ReplayHotkeyError.Length == 0 && controller.RecordHotkeyError.Length == 0, "both page shortcuts activate without modifiers, using fallback when reserved: " + controller.ReplayHotkeyError + " / " + controller.RecordHotkeyError);
            ((System.Windows.Controls.Button)window.FindName("SettingsNav")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var page = (SettingsPage)((System.Windows.Controls.ContentControl)window.FindName("Body")).Content;
            var replay = (System.Windows.Controls.TextBox)page.FindName("ReplayHotkey");
            var record = (System.Windows.Controls.TextBox)page.FindName("RecordHotkey");
            var scroll = (System.Windows.Controls.ScrollViewer)page.FindName("SettingsScroll");
            replay.BringIntoView();
            PumpUntil(() => false, TimeSpan.FromMilliseconds(100));
            replay.Focus();
            PumpUntil(() => replay.IsKeyboardFocused && window.IsActive, TimeSpan.FromSeconds(2));
            Check(replay.IsKeyboardFocused && window.IsActive, "hotkey field receives keyboard focus");
            Check(RegisterTestHotkey(0, 80, HotkeyModifiers.NoRepeat, 0x22), "editing releases the native Page Down shortcut");
            // Keep Page Down reserved to exercise the real conflict fallback.
            using var probe = new HotkeyHost();
            Check(probe.TryRegister(1, new(0, 0x21), out _) && probe.TryRegister(2, new(0, 0x22), out _), "editing releases both global hotkeys so existing keys can be reassigned");
            var pressed = new List<int>();
            probe.Pressed += pressed.Add;
            var source = (System.Windows.Interop.HwndSource)typeof(HotkeyHost).GetField("_source", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(probe)!;
            PostHotkey(source.Handle, 0x0312, 1, 0x21 << 16);
            PostHotkey(source.Handle, 0x0312, 2, 0x22 << 16);
            PumpUntil(() => pressed.Count == 2, TimeSpan.FromSeconds(2));
            Check(pressed.SequenceEqual(new[] { 1, 2 }), "registered page keys dispatch both hotkey IDs through the Windows message handler");
            pressed.Clear();
            var inputs = new[]
            {
                new HotkeyInput { Type = 1, Key = 0x21, Flags = 1 },
                new HotkeyInput { Type = 1, Key = 0x21, Flags = 1 },
                new HotkeyInput { Type = 1, Key = 0x21, Flags = 3 },
                new HotkeyInput { Type = 1, Key = 0x22, Flags = 1 },
                new HotkeyInput { Type = 1, Key = 0x22, Flags = 1 },
                new HotkeyInput { Type = 1, Key = 0x22, Flags = 3 }
            };
            Check(SendHotkeyInputs((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<HotkeyInput>()) == inputs.Length, "Windows accepts page-key input for the isolated test handler");
            PumpUntil(() => pressed.Count >= 2, TimeSpan.FromSeconds(2));
            PumpUntil(() => false, TimeSpan.FromMilliseconds(100));
            Check(pressed.SequenceEqual(new[] { 1, 2 }), "reserved page keys reach the fallback listener and key repeats trigger once");
            probe.Unregister(1);
            probe.Unregister(2);
            UnregisterTestHotkey(0, 80);
            void Press(System.Windows.Controls.TextBox field, System.Windows.Input.Key key)
            {
                var input = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(window)!, Environment.TickCount, key)
                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                field.RaiseEvent(input);
                Check(input.Handled, "page key input is handled instead of scrolling the settings");
            }
            var offset = scroll.VerticalOffset;
            Press(replay, System.Windows.Input.Key.PageDown);
            Check(controller.Settings.ReplayHotkey.VirtualKey == 0x22 && replay.Text == "Page Down" && Math.Abs(scroll.VerticalOffset - offset) < 1, "Page Down is captured with its correct virtual key and visible label");
            record.Focus();
            Press(record, System.Windows.Input.Key.PageUp);
            Check(controller.Settings.RecordHotkey.VirtualKey == 0x21 && record.Text == "Page Up", "Page Up is captured for the recording shortcut");
            ((System.Windows.Controls.Button)window.FindName("SettingsNav")).Focus();
            PumpUntil(() => !page.IsHotkeyEditing, TimeSpan.FromSeconds(1));
            Check(controller.ReplayHotkeyError.Length == 0 && controller.RecordHotkeyError.Length == 0, "swapping page shortcuts registers both without a false conflict");
            Check(!RegisterTestHotkey(0, 80, HotkeyModifiers.NoRepeat, 0x22), "leaving the editor restores the native shortcut and page fallback");
            replay.Focus();
            Check(RegisterTestHotkey(0, 80, HotkeyModifiers.NoRepeat, 0x22), "refocusing the editor pauses shortcuts again");
            UnregisterTestHotkey(0, 80);
            var outside = new System.Windows.Window { Content = new System.Windows.Controls.Button { Content = "Test window" }, Width = 180, Height = 100, ShowInTaskbar = false };
            try
            {
                outside.Show();
                outside.Activate();
                PumpUntil(() => outside.IsActive && !window.IsActive, TimeSpan.FromSeconds(2));
                Check(outside.IsActive && !window.IsActive && !RegisterTestHotkey(0, 80, HotkeyModifiers.NoRepeat, 0x22), "switching away restores shortcuts even if the settings field had focus");
            }
            finally { outside.Close(); }
            var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(controller.Settings))!;
            Check(restored.ReplayHotkey.VirtualKey == 0x22 && restored.RecordHotkey.VirtualKey == 0x21 && restored.ReplayHotkey.Modifiers == 0 && restored.RecordHotkey.Modifiers == 0, "both page key assignments survive serialization");
            Check(!controller.Snapshot().PipelineRunning, "editing hotkeys starts no recording");
            Check((File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null) == originalSettings, "real user settings remain unchanged");
            report.Add("HOTKEY_TEST_OK");
            return 0;
        }
        catch (Exception ex) { report.Add("HOTKEY_TEST_FAILED " + ex); return 1; }
        finally
        {
            UnregisterTestHotkey(0, 80);
            if (window != null)
            {
                typeof(MainWindow).GetField("_exiting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
                window.Close();
            }
            File.WriteAllLines(Path.Combine(directory, "result.txt"), report);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "PostMessageW")]
    static extern bool PostHotkey(IntPtr window, uint message, nint id, nint data);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 40)]
    struct HotkeyInput
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public uint Type;
        [System.Runtime.InteropServices.FieldOffset(8)] public ushort Key;
        [System.Runtime.InteropServices.FieldOffset(12)] public uint Flags;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
    static extern uint SendHotkeyInputs(uint count, [System.Runtime.InteropServices.In] HotkeyInput[] inputs, int size);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    static extern bool RegisterTestHotkey(nint window, int id, uint modifiers, uint key);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "UnregisterHotKey")]
    static extern bool UnregisterTestHotkey(nint window, int id);

    public static int RunGalleryPerformance(string samplePath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpeziGalleryPerformance", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        var library = Path.Combine(directory, "library", "Overwatch");
        Directory.CreateDirectory(library);
        var report = new List<string>();
        System.Windows.Window? window = null;
        try
        {
            for (var i = 0; i < 24; i++) File.Copy(samplePath, Path.Combine(library, $"Overwatch_Clip_Benchmark_{i:D2}.mp4"));
            using var controller = new AppController(new AppSettings { OutputDirectory = Path.GetDirectoryName(library)! }, false, false);
            var gallery = new GalleryPage(controller);
            window = new System.Windows.Window { Content = gallery, Width = 600, Height = 620, ShowInTaskbar = false };
            GC.Collect();
            GC.WaitForPendingFinalizers();
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var beforeMemory = process.PrivateMemorySize64;
            var beforeCpu = process.TotalProcessorTime;
            var beforeAllocations = GC.GetTotalAllocatedBytes(true);
            var clock = Stopwatch.StartNew();
            window.Show();
            gallery.RefreshList();
            var rows = (System.Windows.Controls.ListBox)gallery.FindName("Clips");
            PumpUntil(() => rows.Items.Count == 24 && rows.Items.Cast<GalleryPage.ClipRow>().Take(2).All(row => row.Thumbnail != null), TimeSpan.FromSeconds(15));
            if (rows.Items.Count != 24 || rows.Items.Cast<GalleryPage.ClipRow>().Take(2).Any(row => row.Thumbnail == null)) throw new Exception("Visible previews did not load");
            var ready = clock.Elapsed.TotalMilliseconds;
            PumpUntil(() => false, TimeSpan.FromSeconds(2));
            var elapsed = clock.Elapsed;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            process.Refresh();
            report.Add($"clips=24 initialReadyMs={ready:F0} loadedThumbnails={rows.Items.Cast<GalleryPage.ClipRow>().Count(row => row.Thumbnail != null)} privateMemoryDeltaMB={(process.PrivateMemorySize64 - beforeMemory) / 1048576d:F1} workingSetMB={process.WorkingSet64 / 1048576d:F1} managedAllocatedMB={(GC.GetTotalAllocatedBytes(true) - beforeAllocations) / 1048576d:F1} cpuMs={(process.TotalProcessorTime - beforeCpu).TotalMilliseconds:F0}");
            var first = (GalleryPage.ClipRow)rows.Items[0];
            var firstPreview = first.Thumbnail;
            var last = (GalleryPage.ClipRow)rows.Items[23];
            ((System.Windows.Controls.TextBox)gallery.FindName("SearchBox")).Focus();
            rows.ScrollIntoView(last);
            PumpUntil(() => last.Thumbnail != null && first.Thumbnail == null, TimeSpan.FromSeconds(10));
            var loadedAfterScroll = rows.Items.Cast<GalleryPage.ClipRow>().Count(row => row.Thumbnail != null);
            if (last.Thumbnail == null || first.Thumbnail != null || loadedAfterScroll > 8) throw new Exception($"Scrolling did not recycle previews: last={last.Thumbnail != null}, first={first.Thumbnail != null}, loaded={loadedAfterScroll}");
            rows.ScrollIntoView(first);
            PumpUntil(() => first.Thumbnail != null, TimeSpan.FromSeconds(5));
            if (!ReferenceEquals(firstPreview, first.Thumbnail)) throw new Exception("Returning to a clip did not reuse its compact cached preview");
            report.Add("PASS scrolling loads new visible clips, releases hidden previews and reuses cached previews");
            report.Add("GALLERY_PERFORMANCE_OK");
            return 0;
        }
        catch (Exception ex) { report.Add("GALLERY_PERFORMANCE_FAILED " + ex); return 1; }
        finally { window?.Close(); File.WriteAllLines(Path.Combine(directory, "result.txt"), report); }
    }

    public static int RunToasts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpeziToastTest", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        var toast = new ToastWindow();
        System.Windows.Window? host = null;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.Add("PASS " + message);
        }
        void Capture(string name)
        {
            var visual = (System.Windows.FrameworkElement)toast.Content;
            visual.UpdateLayout();
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            image.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(directory, name));
            encoder.Save(stream);
        }
        try
        {
            host = new System.Windows.Window { Width = 160, Height = 80, ShowInTaskbar = false, Title = "Spezi Toast test", Content = new System.Windows.Controls.TextBlock { Text = "Testing notifications", TextWrapping = System.Windows.TextWrapping.Wrap } };
            host.Show();
            host.Activate();
            PumpUntil(() => false, TimeSpan.FromMilliseconds(150));
            var hostHandle = new System.Windows.Interop.WindowInteropHelper(host).Handle;
            var foreground = ToastWindow.GetForegroundWindow();
            var screen = System.Windows.Forms.Screen.FromHandle(foreground);
            var selection = new CaptureSelection { Kind = CaptureKind.Monitor, MonitorDeviceName = screen.DeviceName };
            toast.ShowNotice(new RecorderNotice { Kind = NoticeKind.ClipSaved, Message = "Clip saved (0:35).", FilePath = @"C:\Videos\Overwatch_Clip_2026-10-06_12-34-56.mp4" }, selection);
            PumpUntil(() => false, TimeSpan.FromMilliseconds(150));
            Check(ToastWindow.GetForegroundWindow() == foreground, "clip notification leaves foreground focus unchanged");
            var title = (System.Windows.Controls.TextBlock)typeof(ToastWindow).GetField("_title", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(toast)!;
            var detail = (System.Windows.Controls.TextBlock)typeof(ToastWindow).GetField("_detail", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(toast)!;
            Check(title.Text == "Clip saved (0:35)", "saved clip notification is in English");
            var handle = new System.Windows.Interop.WindowInteropHelper(toast).Handle;
            var styles = ToastWindow.GetWindowLongPtr(handle, -20).ToInt64();
            Check((styles & 0x080000A0L) == 0x080000A0L, "no activation, no taskbar entry and mouse click-through styles");
            void CheckPosition(System.Windows.Forms.Screen target)
            {
                if (!GetWindowRect(handle, out var bounds)) throw new Exception("Toast bounds unavailable");
                var gap = (int)Math.Round(20 * GetWindowDpi(handle) / 96d);
                Check(Math.Abs(bounds.Top - (target.WorkingArea.Top + gap)) <= 2 && Math.Abs(bounds.Right - (target.WorkingArea.Right - gap)) <= 2, "top-right placement on " + target.DeviceName);
            }
            CheckPosition(screen);
            Capture("toast-clip.png");
            toast.ShowNotice(new RecorderNotice { Kind = NoticeKind.ReplayStarted, Message = "Replay is on. Clip length: 5m" }, selection);
            PumpUntil(() => false, TimeSpan.FromMilliseconds(150));
            Check(ToastWindow.GetForegroundWindow() == foreground, "replay notification leaves foreground focus unchanged");
            Check(title.Text == "Replay on" && detail.Text == "Clip length: 5m", "replay notification uses concise English text");
            Capture("toast-replay.png");
            if (foreground == hostHandle)
            {
                foreach (var target in System.Windows.Forms.Screen.AllScreens)
                {
                    toast.ShowNotice(new RecorderNotice { Kind = NoticeKind.Information, Message = "Display position" }, new CaptureSelection { Kind = CaptureKind.Monitor, MonitorDeviceName = target.DeviceName });
                    PumpUntil(() => false, TimeSpan.FromMilliseconds(100));
                    CheckPosition(target);
                }
            }
            toast.ShowNotice(new RecorderNotice { Kind = NoticeKind.Error, Message = "The save location is unavailable. Check the folder in Settings." }, selection);
            Capture("toast-error.png");
            toast.ShowNotice(new RecorderNotice { Kind = NoticeKind.ClipSaved, Message = "Clip saved (5:00).", FilePath = "C:\\Videos\\" + new string('W', 180) + ".mp4" }, selection);
            Capture("toast-long-name.png");
            PumpUntil(() => false, TimeSpan.FromSeconds(2));
            toast.ShowMessage("A new notification replaces the previous one.");
            PumpUntil(() => false, TimeSpan.FromSeconds(1.7));
            Check(toast.IsVisible, "replacement notification gets its full display time");
            PumpUntil(() => !toast.IsVisible, TimeSpan.FromSeconds(2.2));
            Check(!toast.IsVisible, "notification automatically hides after 3.5 seconds");
            toast.ShowMessage("Shown again");
            Check(toast.IsVisible, "hidden notification window can be reused");
            var customized = new AppSettings { ToastDurationSeconds = 1, AccentColor = "#69B7FF" };
            var elapsed = Stopwatch.StartNew();
            toast.ShowNotice(new RecorderNotice { Kind = NoticeKind.ClipSaved, Message = "Clip saved (0:35).", FilePath = @"C:\Videos\Overwatch\Overwatch_Clip_2026-10-06_12-34-56.mp4" }, selection, customized);
            Capture("toast-blue.png");
            PumpUntil(() => !toast.IsVisible, TimeSpan.FromSeconds(2));
            Check(!toast.IsVisible && elapsed.Elapsed.TotalSeconds is >= 0.85 and < 1.8, "configured one-second toast duration is applied");
            customized.ToastDurationSeconds = 5;
            toast.ShowMessage("Longer display duration", customized);
            PumpUntil(() => false, TimeSpan.FromSeconds(1.2));
            Check(toast.IsVisible, "longer duration replaces the previous one-second duration");
            report.Add("TOAST_TEST_OK");
            return 0;
        }
        catch (Exception ex) { report.Add("TOAST_TEST_FAILED " + ex); return 1; }
        finally { toast.Close(); host?.Close(); File.WriteAllLines(Path.Combine(directory, "result.txt"), report); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct WindowBounds { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out WindowBounds rect);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    static extern uint GetWindowDpi(IntPtr hwnd);

    public static int RunSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpeziSettingsTest", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        System.Windows.Window? window = null;
        var originalSettings = File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null;
        try
        {
            using var controller = new AppController(SettingsStore.Load(), false, false);
            var page = new SettingsPage(controller);
            window = new System.Windows.Window { Content = page, Width = 460, Height = 780, ShowInTaskbar = false, Title = "Spezi Einstellungen", Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("Bg") };
            window.Show();
            page.Reload();
            page.LoadAudioNow();
            PumpUntil(() => false, TimeSpan.FromMilliseconds(250));
            void Capture(string name, System.Windows.FrameworkElement visual)
            {
                visual.UpdateLayout();
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                var drawing = new System.Windows.Media.DrawingVisual();
                using (var context = drawing.RenderOpen())
                    context.DrawRectangle(new System.Windows.Media.VisualBrush(visual), null, new System.Windows.Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
                image.Render(drawing);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var stream = File.Create(Path.Combine(directory, name));
                encoder.Save(stream);
            }
            var durationSlider = (System.Windows.Controls.Slider)page.FindName("ToastDurationSlider");
            durationSlider.Value = 7.5;
            if (controller.Settings.ToastDurationSeconds != 7.5) throw new Exception("Toast duration control did not save its value");
            ((System.Windows.Controls.Button)page.FindName("BlueAccentButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            PumpUntil(() => false, TimeSpan.FromMilliseconds(100));
            var diskText = (System.Windows.Controls.TextBlock)page.FindName("DiskText");
            if (controller.Settings.AccentColor != "#69B7FF" || ((System.Windows.Media.SolidColorBrush)diskText.Foreground).Color != AppTheme.Parse("#69B7FF")) throw new Exception("Existing screen did not adopt the selected accent color");
            var saved = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(controller.Settings))!;
            if (saved.AccentColor != "#69B7FF" || saved.ToastDurationSeconds != 7.5) throw new Exception("Appearance preferences do not survive serialization");
            Capture("settings-appearance.png", (System.Windows.FrameworkElement)page.FindName("AppearanceCard"));
            Capture("settings-top.png", window);
            var scroll = (System.Windows.Controls.ScrollViewer)page.FindName("SettingsScroll");
            Capture("settings-full.png", (System.Windows.FrameworkElement)scroll.Content);
            scroll.ScrollToVerticalOffset(710);
            PumpUntil(() => false, TimeSpan.FromMilliseconds(100));
            Capture("settings-middle.png", window);
            foreach (var pair in new[] { ("EconomyButton", QualityProfile.Economy), ("BalancedButton", QualityProfile.Balanced), ("HighButton", QualityProfile.High) })
            {
                ((System.Windows.Controls.Button)page.FindName(pair.Item1)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (controller.Settings.Profile != pair.Item2) throw new Exception("Quality profile selection failed");
            }
            var systemAudio = (System.Windows.Controls.CheckBox)page.FindName("SystemAudioBox");
            systemAudio.IsChecked = false;
            if (controller.Settings.CaptureSystemAudio != false) throw new Exception("System audio switch failed");
            systemAudio.IsChecked = true;
            if (controller.Settings.CaptureSystemAudio != true) throw new Exception("System audio switch failed");
            var resolution = (System.Windows.Controls.ComboBox)page.FindName("ResolutionBox");
            resolution.SelectedItem = "1280×720";
            if (controller.Settings.Width != 1280 || controller.Settings.Height != 720) throw new Exception("Custom resolution selection failed");
            scroll.ScrollToBottom();
            PumpUntil(() => false, TimeSpan.FromMilliseconds(100));
            Capture("settings-bottom.png", window);
            if (File.Exists(SettingsStore.FilePath) && File.ReadAllText(SettingsStore.FilePath) != originalSettings) throw new Exception("Settings test changed user preferences");
            report.Add("PASS profiles, source, system audio switch and custom resolution load correctly");
            report.Add("PASS toast duration, palette selection, immediate accent update and preference serialization");
            report.Add("PASS real user settings remain unchanged");
            report.Add("SETTINGS_TEST_OK");
            return 0;
        }
        catch (Exception ex) { report.Add("SETTINGS_TEST_FAILED " + ex); return 1; }
        finally { window?.Close(); File.WriteAllLines(Path.Combine(directory, "result.txt"), report); }
    }

    public static int RunMedia(string path, string? libraryRoot = null)
    {
        var report = new List<string>();
        var directory = Path.Combine(Path.GetTempPath(), "SpeziMediaTest", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.Add("PASS " + message);
        }
        System.Windows.Window? galleryWindow = null;
        try
        {
            using var controller = new AppController(new AppSettings { OutputDirectory = libraryRoot ?? Path.GetDirectoryName(path)!, AutoReplayWhenPlaying = false }, false, false);
            controller.StartReplay();
            Check(controller.IsBusy, "gallery test runs while replay capture is active");
            var gallery = new GalleryPage(controller);
            galleryWindow = new System.Windows.Window { Content = gallery, Width = 600, Height = 620, ShowInTaskbar = false };
            galleryWindow.Show();
            gallery.RefreshList();
            var rows = (System.Windows.Controls.ListBox)gallery.FindName("Clips");
            PumpUntil(() => rows.Items.Cast<GalleryPage.ClipRow>().Any(r => r.Path == path), TimeSpan.FromSeconds(5));
            var row = rows.Items.Cast<GalleryPage.ClipRow>().FirstOrDefault(r => r.Path == path);
            if (row != null) rows.ScrollIntoView(row);
            PumpUntil(() => row?.Thumbnail != null && row.DurationText != "…", TimeSpan.FromSeconds(12));
            Check(row?.Thumbnail != null && row.DurationText != "…" && row.DurationText != "–", "actual gallery row loads thumbnail and duration");
            var thumbnail = (System.Windows.Media.Imaging.BitmapSource)row!.Thumbnail!;
            var pixels = new byte[thumbnail.PixelWidth * thumbnail.PixelHeight * 4];
            thumbnail.CopyPixels(pixels, thumbnail.PixelWidth * 4, 0);
            Check(pixels.Where((_, index) => index % 4 == 3).All(alpha => alpha == 255), "gallery thumbnail is opaque");
            Check(thumbnail.PixelWidth <= 320, "gallery preview uses a small cached image");
            Check(thumbnail is not System.Windows.Media.Imaging.TransformedBitmap, "preview does not retain its full-resolution source");
            var search = (System.Windows.Controls.TextBox)gallery.FindName("SearchBox");
            search.Text = libraryRoot == null ? Path.GetFileNameWithoutExtension(path) : Path.GetRelativePath(libraryRoot, Path.GetDirectoryName(path)!);
            PumpUntil(() => rows.Items.Cast<GalleryPage.ClipRow>().Any(r => r.Path == path && !ReferenceEquals(r, row)), TimeSpan.FromSeconds(5));
            Check(rows.Items.Cast<GalleryPage.ClipRow>().Any(r => r.Path == path && !ReferenceEquals(r, row)), "gallery search finds a nested video by its game folder");
            search.Text = Guid.NewGuid().ToString("N");
            PumpUntil(() => rows.Items.Count == 0, TimeSpan.FromSeconds(5));
            Check(rows.Items.Count == 0, "gallery search excludes non-matching videos");
            search.Text = "";
            PumpUntil(() => rows.Items.Cast<GalleryPage.ClipRow>().Any(r => r.Path == path), TimeSpan.FromSeconds(5));
            Check(rows.Items.Cast<GalleryPage.ClipRow>().Any(r => r.Path == path), "clearing gallery search restores nested videos");
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(thumbnail));
            using (var stream = File.Create(Path.Combine(directory, "gallery-thumbnail.png"))) encoder.Save(stream);
            var capture = new CapturePage(controller);
            galleryWindow.Content = capture;
            var recent = (System.Windows.Controls.ItemsControl)capture.FindName("RecentList");
            PumpUntil(() => recent.Items.Count > 0 && recent.Items.Cast<CapturePage.RecentClip>().All(clip => clip.Thumbnail != null && clip.DurationText != "…"), TimeSpan.FromSeconds(12));
            Check(recent.Items.Count > 0 && recent.Items.Cast<CapturePage.RecentClip>().All(clip => clip.Thumbnail is System.Windows.Media.Imaging.BitmapSource image && image.PixelWidth <= 320 && clip.DurationText != "…" && clip.DurationText != "–"), "recent clips load compact previews and durations while replay is active");
            report.Add("MEDIA_TEST_OK");
            return 0;
        }
        catch (Exception ex)
        {
            report.Add("MEDIA_TEST_FAILED " + ex);
            return 1;
        }
        finally
        {
            galleryWindow?.Close();
            File.WriteAllLines(Path.Combine(directory, "result.txt"), report);
        }
    }

    static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) => { if (condition() || elapsed.Elapsed >= timeout) frame.Continue = false; };
        timer.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    public static int Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpeziSelfTest");
        Directory.CreateDirectory(directory);
        using var recorder = new Recorder();
        var displays = recorder.ListDisplays();
        if (displays.Count == 0)
        {
            Console.WriteLine("Kein Bildschirm.");
            return 1;
        }

        var display = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
        var settings = new AppSettings { AllowSoftwareEncoder = true, OutputDirectory = directory, CaptureCursor = false };
        AppSettings.ApplyProfile(settings, QualityProfile.Economy, display.Width, display.Height);
        var outputs = recorder.ListAudioDevices().Where(d => !d.IsMicrophone && d.IsDefault).Select(d => d.Id).ToList();
        settings.OutputDeviceIds = outputs;
        var request = new RecordingRequest
        {
            Settings = settings,
            Capture = new CaptureSelection { Kind = CaptureKind.Monitor, MonitorDeviceName = display.DeviceName },
            ReplayDuration = TimeSpan.FromSeconds(8)
        };

        var gpuBefore = QueryGpu();
        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        recorder.StartReplay(request);
        Thread.Sleep(TimeSpan.FromSeconds(3));
        recorder.StartSession(request);
        Thread.Sleep(TimeSpan.FromSeconds(4));
        var snapshot = recorder.GetSnapshot();
        var gpuDuring = QueryGpu();
        process.Refresh();
        var cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / (wall.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100.0;
        var memory = process.WorkingSet64;
        var clip = recorder.SaveClipAsync().GetAwaiter().GetResult();
        var session = recorder.StopSessionAsync().GetAwaiter().GetResult();
        recorder.StopReplay();

        var clipProbe = clip != null && File.Exists(clip) ? MediaInspector.Probe(clip) : default;
        var sessionProbe = session != null && File.Exists(session) ? MediaInspector.Probe(session) : default;
        Console.WriteLine($"encoder={snapshot.EncoderName}");
        Console.WriteLine($"encodedFps={snapshot.EncodedFps:0.0} dropped={snapshot.DroppedFrames} replay={snapshot.ReplayBuffered}");
        Console.WriteLine($"cpuPercent={cpu:0.0} workingSetMB={memory / 1024d / 1024:0}");
        Console.WriteLine($"gpuBefore={gpuBefore}");
        Console.WriteLine($"gpuDuring={gpuDuring}");
        Console.WriteLine($"clip video={clipProbe.HasVideo} audio={clipProbe.HasAudio} duration={clipProbe.Duration} bytes={(clip != null && File.Exists(clip) ? new FileInfo(clip).Length : 0)}");
        Console.WriteLine($"session video={sessionProbe.HasVideo} audio={sessionProbe.HasAudio} duration={sessionProbe.Duration} bytes={(session != null && File.Exists(session) ? new FileInfo(session).Length : 0)}");
        var ok = clipProbe.HasVideo && clipProbe.HasAudio && sessionProbe.HasVideo && sessionProbe.HasAudio
                 && clipProbe.Duration > TimeSpan.FromSeconds(4)
                 && sessionProbe.Duration > TimeSpan.FromSeconds(2);
        var report = string.Join(Environment.NewLine, new[]
        {
            $"encoder={snapshot.EncoderName}",
            $"encodedFps={snapshot.EncodedFps:0.0} dropped={snapshot.DroppedFrames} replay={snapshot.ReplayBuffered}",
            $"cpuPercent={cpu:0.0} workingSetMB={memory / 1024d / 1024:0}",
            $"gpuBefore={gpuBefore}",
            $"gpuDuring={gpuDuring}",
            $"clip video={clipProbe.HasVideo} audio={clipProbe.HasAudio} duration={clipProbe.Duration} bytes={(clip != null && File.Exists(clip) ? new FileInfo(clip).Length : 0)}",
            $"session video={sessionProbe.HasVideo} audio={sessionProbe.HasAudio} duration={sessionProbe.Duration} bytes={(session != null && File.Exists(session) ? new FileInfo(session).Length : 0)}",
            ok ? "SELFTEST_OK" : "SELFTEST_FAILED"
        });
        File.WriteAllText(Path.Combine(directory, "result.txt"), report);
        Console.WriteLine(report);
        return ok ? 0 : 1;
    }

    static string QueryGpu()
    {
        try
        {
            var start = new ProcessStartInfo("nvidia-smi", "--query-gpu=utilization.gpu,utilization.encoder,memory.used --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(start);
            if (process == null)
                return "n/a";
            var text = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return text.Replace('\n', ' ');
        }
        catch
        {
            return "n/a";
        }
    }
}
