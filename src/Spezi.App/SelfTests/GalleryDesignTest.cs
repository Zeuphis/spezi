using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spezi.Core;

namespace Spezi.App;

static partial class SelfTest
{
    public static int RunGalleryDesign(string samplePath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpeziGalleryDesign", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        var library = Path.Combine(directory, "library");
        Directory.CreateDirectory(library);
        var report = new List<string>();
        MainWindow? window = null;
        var originalSettings = File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null;
        void Check(bool value, string text) { if (!value) throw new Exception(text); report.Add("PASS " + text); }
        void Capture(string name)
        {
            window!.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(window), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
        }
        T? Child<T>(DependencyObject parent, Func<T, bool> match) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && match(typed)) return typed;
                if (Child(child, match) is { } found) return found;
            }
            return null;
        }
        try
        {
            var epoch = new DateTime(2026, 10, 9, 14, 30, 0);
            for (var i = 0; i < 6; i++)
            {
                var game = i % 2 == 0 ? "Overwatch" : "Rainbow Six Siege";
                var folder = Path.Combine(library, game); Directory.CreateDirectory(folder);
                var name = i == 5 ? "Final round clutch.mp4" : $"{game}_Clip_2026-10-09_14-{30 + i:D2}-00-000_0123456789ab.mp4";
                var path = Path.Combine(folder, name); File.Copy(samplePath, path);
                File.SetLastWriteTime(path, epoch.AddMinutes(i));
            }
            using var controller = new AppController(new AppSettings
            {
                OutputDirectory = library, AutoReplayWhenPlaying = false, StatusToasts = false,
                ReplayHotkey = new(0, 0x7C), RecordHotkey = new(0, 0x7D)
            }, false, false);
            window = new MainWindow(controller); window.Show();
            ((System.Windows.Controls.Button)window.FindName("GalleryNav")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var gallery = (GalleryPage)((ContentControl)window.FindName("Body")).Content;
            var list = (System.Windows.Controls.ItemsControl)gallery.FindName("Clips");
            var search = (System.Windows.Controls.TextBox)gallery.FindName("SearchBox");
            var clear = (System.Windows.Controls.Button)gallery.FindName("ClearSearchButton");
            var label = (TextBlock)gallery.FindName("LibraryText");
            PumpUntil(() => gallery.FilteredRows.Count == 6 && gallery.FilteredRows.Take(2).All(row => row.Thumbnail != null), TimeSpan.FromSeconds(12));
            Check(gallery.FilteredRows.Count == 6 && gallery.FilteredRows.Take(2).All(row => row.Thumbnail != null), "real MP4 thumbnails and durations load in the new cards");
            Check(label.Text == "6 clips · 2 games" && gallery.FilteredRows.First().DisplayName == "Final round clutch", "library count and custom clip names are preserved");
            Check(gallery.FilteredRows.Skip(1).All(row => row.DisplayName.StartsWith("Replay · ")), "generated filenames use readable display titles without renaming files");
            Check(search.ActualHeight <= 34 && clear.Visibility == Visibility.Collapsed, "search is compact and its clear control is hidden until needed");
            Capture("gallery-cards.png");
            var menuButton = (System.Windows.Controls.Button)gallery.FindName("ClipMenuButton");
            Check(menuButton != null && menuButton.ContextMenu != null, "each clip has a compact actions menu");
            menuButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            PumpUntil(() => menuButton.ContextMenu!.IsOpen, TimeSpan.FromSeconds(1));
            var menu = menuButton.ContextMenu!;
            Check(menu.IsOpen && ReferenceEquals(menu.DataContext, gallery.FilteredRows[0]) && menu.Items.OfType<MenuItem>().All(item => ReferenceEquals(item.DataContext, gallery.FilteredRows[0])), "menu actions retain the correct clip after the popup opens");
            Check(menu.Items.OfType<MenuItem>().Select(item => item.Header.ToString()).SequenceEqual(new[] { "Open in default player", "Rename", "Open folder", "Delete" }), "existing player and file actions remain available");
            menu.IsOpen = false;
            search.Text = "overwatch";
            PumpUntil(() => gallery.FilteredRows.Count == 3 && label.Text == "3 matches", TimeSpan.FromSeconds(5));
            Check(gallery.FilteredRows.Count == 3 && gallery.FilteredRows.All(row => row.Game == "Overwatch") && clear.Visibility == Visibility.Visible,
                "search filters clips by game and exposes clear search");
            var sort = (System.Windows.Controls.Button)gallery.FindName("SortButton"); sort.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            PumpUntil(() => gallery.FilteredRows.Count == 3 && gallery.FilteredRows.First().Name.Contains("14-30"), TimeSpan.FromSeconds(5));
            Check(((TextBlock)gallery.FindName("SortLabel")).Text == "Oldest" && gallery.FilteredRows.First().Name.Contains("14-30"), "compact sort control orders matching clips correctly");
            search.Text = "no-matching-game";
            PumpUntil(() => label.Text == "0 matches", TimeSpan.FromSeconds(5));
            Check(((FrameworkElement)gallery.FindName("EmptyState")).Visibility == Visibility.Visible && list.Visibility == Visibility.Collapsed, "no-results state replaces the cards");
            Capture("gallery-no-results.png");
            clear.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            PumpUntil(() => gallery.FilteredRows.Count == 6 && gallery.FilteredRows.Take(2).All(row => row.Thumbnail != null), TimeSpan.FromSeconds(5));
            Check(gallery.FilteredRows.Count == 6 && search.Text == "" && ((FrameworkElement)gallery.FindName("SearchPlaceholder")).Visibility == Visibility.Visible, "clear search restores the library and placeholder");
            window.Width = 400; window.UpdateLayout();
            PumpUntil(() => gallery.FilteredRows.Take(2).All(row => row.Thumbnail != null), TimeSpan.FromSeconds(5));
            Check(search.ActualWidth > 100 && sort.ActualWidth == 96, "toolbar fits the minimum application width");
            Capture("gallery-narrow.png");
            controller.Settings.AccentColor = "#AC8BFA"; controller.Save();
            window.Width = 600; window.UpdateLayout();
            Capture("gallery-wide-violet.png");
            var emptyFolder = Path.Combine(directory, "empty"); Directory.CreateDirectory(emptyFolder);
            controller.Settings.OutputDirectory = emptyFolder; controller.Save(); gallery.RefreshList();
            PumpUntil(() => gallery.FilteredRows.Count == 0 && label.Text == "Your saved moments.", TimeSpan.FromSeconds(5));
            Check(gallery.FilteredRows.Count == 0 && ((TextBlock)gallery.FindName("EmptyTitle")).Text == "Your first highlight awaits", "a fresh library has a complete empty state");
            Capture("gallery-empty.png");
            Check((File.Exists(SettingsStore.FilePath) ? File.ReadAllText(SettingsStore.FilePath) : null) == originalSettings, "real user settings remain unchanged");
            report.Add("GALLERY_DESIGN_OK"); return 0;
        }
        catch (Exception ex) { report.Add("GALLERY_DESIGN_FAILED " + ex); return 1; }
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
