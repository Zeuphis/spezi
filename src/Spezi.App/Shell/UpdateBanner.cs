using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Spezi.Core;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;

namespace Spezi.App;

// Non-intrusive "update available" banner at the bottom of the main window.
public partial class MainWindow
{
    Border? _updateBanner;

    internal async void CheckForUpdatesOnStartup()
    {
        if (!_controller.Settings.CheckForUpdatesOnStartup) return;
        try
        {
            using var http = UpdateChecker.CreateClient();
            var update = await UpdateChecker.CheckAsync(http, UpdateChecker.CurrentVersion);
            if (update != null) ShowUpdateBanner(update);
        }
        catch (Exception ex) { AppLog.Error("Update check failed.", ex); }
    }

    internal void ShowUpdateBanner(AvailableUpdate update)
    {
        if (_updateBanner != null || Content is not Grid root) return;
        WpfBrush Res(string key) => (WpfBrush)FindResource(key);
        var text = new TextBlock
        {
            Text = $"Spezi {update.Version.ToString(3)} is available.",
            Foreground = Res("Ink"), FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        };
        var updateButton = new WpfButton { Content = "Update", Style = (Style)FindResource("SmallButton"), Margin = new Thickness(10, 0, 6, 0) };
        var laterButton = new WpfButton { Content = "Later", Style = (Style)FindResource("SmallButton") };
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(updateButton, 1); Grid.SetColumn(laterButton, 2);
        panel.Children.Add(text); panel.Children.Add(updateButton); panel.Children.Add(laterButton);
        _updateBanner = new Border
        {
            Background = Res("Card"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 10, 10), Margin = new Thickness(16), MaxWidth = 560,
            VerticalAlignment = VerticalAlignment.Bottom, Child = panel,
        };
        System.Windows.Automation.AutomationProperties.SetName(_updateBanner, "Update available");
        Grid.SetRow(_updateBanner, 2);
        System.Windows.Controls.Panel.SetZIndex(_updateBanner, 10);
        root.Children.Add(_updateBanner);
        laterButton.Click += (_, _) => { root.Children.Remove(_updateBanner); };
        updateButton.Click += async (_, _) =>
        {
            var state = _controller.Snapshot();
            if (state.SessionActive || state.ReplayActive)
            {
                text.Text = "Stop replay and recordings first, then click Update.";
                return;
            }
            updateButton.IsEnabled = laterButton.IsEnabled = false;
            text.Text = $"Downloading Spezi {update.Version.ToString(3)}...";
            try
            {
                using var http = UpdateChecker.CreateClient();
                var file = await UpdateChecker.DownloadVerifiedAsync(http, update, Path.Combine(Path.GetTempPath(), "Spezi", "update"));
                AppLog.Info($"Starting verified update {file}");
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                ExitApp();
            }
            catch (Exception ex)
            {
                AppLog.Error("Update download failed.", ex);
                text.Text = "The update could not be downloaded or verified. Try again later.";
                updateButton.IsEnabled = laterButton.IsEnabled = true;
            }
        };
    }
}
