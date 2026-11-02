using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spezi.Core;

namespace Spezi.App;

static partial class SelfTest
{
    // Renders the main pages to PNGs (normal + maximized) with isolated settings. Never touches user settings.
    public static int RunUiScreenshots(string directory)
    {
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        MainWindow? window = null;
        var originalSettings = File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null;
        void Capture(string name) => CaptureWindow(window!, name);
        void CaptureWindow(Window window, string name)
        {
            window.UpdateLayout();
            PumpUntil(() => false, TimeSpan.FromMilliseconds(400));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(window), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
            report.Add("PNG " + name);
        }
        void Click(string name) => ((System.Windows.Controls.Button)window!.FindName(name)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        try
        {
            var library = Path.Combine(directory, "library"); Directory.CreateDirectory(library);
            // Same key for both hotkeys on purpose: shows the conflict hint (must appear once).
            using var controller = new AppController(new AppSettings
            {
                OutputDirectory = library, AutoReplayWhenPlaying = false, StatusToasts = false,
                ReplayHotkey = new(0, 0x22), RecordHotkey = new(0, 0x22)
            }, false, false);
            window = new MainWindow(controller); window.Show();
            Click("CaptureNav");
            Capture("recording-normal.png");
            window.WindowState = WindowState.Maximized;
            Capture("recording-maximized.png");
            Click("SettingsNav");
            Capture("settings-maximized.png");
            window.WindowState = WindowState.Normal;
            var settings = (SettingsPage)((ContentControl)window.FindName("Body")).Content;
            Capture("settings-collapsed.png");
            settings.ToggleSection(0);
            Capture("settings-expanded.png");
            settings.ToggleSection(0);
            for (var i = 0; i < 7; i++) settings.ToggleSection(i);
            ((FrameworkElement)settings.FindName("SystemAudioBox")).BringIntoView();
            Capture("settings-audio.png");
            ((System.Windows.Controls.ScrollViewer)settings.FindName("SettingsScroll")).ScrollToBottom();
            Capture("settings-bottom.png");
            Click("GalleryNav");
            window.WindowState = WindowState.Maximized;
            Capture("gallery-maximized.png");
            var picker = new ColorPickerWindow(System.Windows.Media.Color.FromRgb(0xAC, 0x8B, 0xFA)) { Owner = window };
            picker.Show();
            CaptureWindow(picker, "color-picker.png");
            ((System.Windows.Controls.TextBox)typeof(ColorPickerWindow).GetField("_hex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(picker)!).Text = "#3FC1E0";
            CaptureWindow(picker, "color-picker-hex.png");
            report.Add(picker.SelectedHex == "#3FC1E0" ? "PASS hex input drives the picker" : "FAIL hex sync " + picker.SelectedHex);
            picker.Close();
            report.Add((File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null) == originalSettings ? "PASS user settings unchanged" : "WARN user settings changed");
            report.Add("UI_SCREENSHOTS_OK"); return 0;
        }
        catch (Exception ex) { report.Add("UI_SCREENSHOTS_FAILED " + ex); return 1; }
        finally
        {
            if (window != null)
            {
                typeof(MainWindow).GetField("_exiting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
                window.Close();
            }
            File.WriteAllLines(Path.Combine(directory, "result.txt"), report);
        }
    }
}
