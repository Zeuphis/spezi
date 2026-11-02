using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Spezi.Core;

namespace Spezi.App;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    readonly AppController _controller;
    readonly DispatcherTimer _levels;
    bool _loading = true;
    internal event Action<bool>? HotkeyEditingChanged;
    internal bool IsHotkeyEditing => ReplayHotkey.IsKeyboardFocusWithin || RecordHotkey.IsKeyboardFocusWithin;

    public SettingsPage(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Unloaded += (_, _) => HotkeyEditingChanged?.Invoke(false);
        FpsBox.ItemsSource = new[] { "30", "60", "120" };
        _levels = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _levels.Tick += (_, _) => PaintLevels();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Reload();
                _levels.Start();
            }
            else
            {
                _levels.Stop();
                if (_controller.Snapshot().AudioTestActive)
                    _controller.StopAudioTest();
            }
        };
    }

    public void Reload()
    {
        _loading = true;
        var settings = _controller.Settings;
        FolderBox.Text = settings.OutputDirectory;
        var free = _controller.FreeBytes();
        DiskText.Text = free <= 0
            ? "Free disk space is unavailable."
            : $"Free: {free / 1024d / 1024 / 1024:0.0} GB" + (free < 8L * 1024 * 1024 * 1024 ? "  ·  Disk space is running low." : "");
        QualityText.Text = $"{ProfileName(settings.Profile)}   {settings.Width}×{settings.Height}   {settings.FrameRate} FPS   {settings.VideoBitrateKbps} kbps";
        EconomyButton.Tag = settings.Profile == QualityProfile.Economy ? "on" : null;
        BalancedButton.Tag = settings.Profile == QualityProfile.Balanced ? "on" : null;
        HighButton.Tag = settings.Profile == QualityProfile.High ? "on" : null;
        var currentResolution = $"{settings.Width}×{settings.Height}";
        var resolutions = new List<string> { "1280×720", "1600×900", "1920×1080", "2560×1440" };
        if (!resolutions.Contains(currentResolution))
            resolutions.Insert(0, currentResolution);
        ResolutionBox.ItemsSource = resolutions;
        ResolutionBox.SelectedItem = currentResolution;
        FpsBox.SelectedItem = settings.FrameRate.ToString();
        LoadSources();
        SoftwareBox.IsChecked = settings.AllowSoftwareEncoder;
        SystemAudioBox.IsChecked = settings.CaptureSystemAudio != false;
        ReplayDuration.Seconds = settings.ReplaySeconds;
        AudioModeBox.SelectedIndex = settings.AudioMode == AudioCaptureMode.Applications ? 1 : 0;
        AudioModeBox.IsEnabled = settings.CaptureSystemAudio != false;
        ApplicationsModeItem.IsEnabled = _controller.ApplicationAudioSupported;
        ApplicationAudioSupportText.Visibility = _controller.ApplicationAudioSupported ? Visibility.Collapsed : Visibility.Visible;
        ApplicationAudioSupportText.Text = "App-specific audio requires Windows 11. You can capture all system audio on this PC.";
        CursorBox.IsChecked = settings.CaptureCursor;
        ReplayHotkey.Text = HotkeyText.Format(settings.ReplayHotkey);
        RecordHotkey.Text = HotkeyText.Format(settings.RecordHotkey);
        HotkeyError.Text = string.Join(" ", new[] { _controller.ReplayHotkeyError, _controller.RecordHotkeyError }.Where(s => s.Length > 0));
        StartupBox.IsChecked = settings.StartWithWindows;
        AutoBox.IsChecked = settings.AutoReplayWhenPlaying;
        ToastBox.IsChecked = settings.StatusToasts;
        ToastDurationSlider.Value = settings.ToastDurationSeconds;
        ToastDurationText.Text = $"{settings.ToastDurationSeconds:0.#} seconds";
        PaintAccentChoice();
        var hardware = _controller.Encoders();
        EncoderText.Text = hardware.Count == 0
            ? "No hardware encoder detected. Enable software encoding below to record."
            : "Detected hardware encoder: " + string.Join(", ", hardware.Select(e => e.Name));
        BuildAudio();
        BuildApplications();
        _loading = false;
        _controller.NoteDiskSpace();
    }

    void BuildAudio()
    {
        AudioList.Children.Clear();
        var selected = _controller.Settings.OutputDeviceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var device in _controller.AudioDevices().Where(d => d.IsMicrophone || _controller.Settings.AudioMode == AudioCaptureMode.System))
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 10) };
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            var toggle = new System.Windows.Controls.CheckBox
            {
                Content = new TextBlock { Text = device.Name, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 6, 0) },
                Tag = device,
                IsChecked = device.IsMicrophone
                    ? _controller.Settings.RecordMicrophone && (_controller.Settings.MicrophoneId == device.Id || _controller.Settings.MicrophoneId == null && device.IsDefault)
                    : selected.Count == 0 ? device.IsDefault : selected.Contains(device.Id),
                IsEnabled = device.IsMicrophone || _controller.Settings.CaptureSystemAudio != false
            };
            toggle.Checked += AudioToggled;
            toggle.Unchecked += AudioToggled;
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 200,
                Value = _controller.Settings.GainFor(device.Id) * 100,
                Tag = device.Id,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            };
            slider.ValueChanged += GainChanged;
            var meter = new System.Windows.Controls.ProgressBar { Height = 8, Maximum = 1, Tag = device.Id, VerticalAlignment = VerticalAlignment.Center };
            meter.SetResourceReference(System.Windows.Controls.ProgressBar.ForegroundProperty, "Accent");
            Grid.SetRow(slider, 1);
            Grid.SetColumn(meter, 1);
            Grid.SetRow(meter, 1);
            var deviceLabel = new TextBlock { Text = device.IsMicrophone ? "MICROPHONE" : "SYSTEM AUDIO", FontSize = 9, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 4) };
            var heading = new StackPanel();
            heading.Children.Add(deviceLabel);
            heading.Children.Add(toggle);
            Grid.SetColumnSpan(heading, 2);
            row.Children.Add(heading);
            row.Children.Add(slider);
            row.Children.Add(meter);
            var wrapper = new Border { Background = (System.Windows.Media.Brush)FindResource("Field"), CornerRadius = new CornerRadius(9), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Child = row };
            row.Margin = new Thickness(0);
            AudioList.Children.Add(wrapper);
        }
    }

    void PaintLevels()
    {
        var levels = _controller.Snapshot().Levels;
        foreach (var row in AudioList.Children.OfType<Border>().Concat(ApplicationAudioList.Children.OfType<Border>()).Select(b => b.Child).OfType<Grid>())
        {
            var meter = row.Children.OfType<System.Windows.Controls.ProgressBar>().FirstOrDefault();
            if (meter?.Tag is not string id)
                continue;
            meter.Value = Math.Min(1, levels.Where(item => item.DeviceId == id).Sum(item => item.Peak));
        }
        TestButton.Content = _controller.Snapshot().AudioTestActive ? "Stop test" : "Test audio";
    }

    void BrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Clip save location",
            SelectedPath = _controller.Settings.OutputDirectory,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;
        _controller.Settings.OutputDirectory = dialog.SelectedPath;
        _controller.Save();
        Reload();
    }

    void UseEconomy(object sender, RoutedEventArgs e) => Apply(QualityProfile.Economy);
    void UseBalanced(object sender, RoutedEventArgs e) => Apply(QualityProfile.Balanced);
    void UseHigh(object sender, RoutedEventArgs e) => Apply(QualityProfile.High);

    void Apply(QualityProfile profile)
    {
        _controller.ApplyProfile(profile);
        Reload();
    }

    void ResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ResolutionBox.SelectedItem is not string text)
            return;
        var parts = text.Split('×');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
            return;
        _controller.Settings.Width = width;
        _controller.Settings.Height = height;
        _controller.Settings.Profile = QualityProfile.Custom;
        _controller.Save();
        Reload();
    }

    void LoadSources()
    {
        SourceBox.Items.Clear();
        foreach (var display in _controller.Displays())
        {
            var choice = new SourceChoice(display.Name + $"  {display.Width}×{display.Height}", display, null);
            SourceBox.Items.Add(choice);
            if (_controller.Capture.Kind == CaptureKind.Monitor &&
                display.DeviceName.Equals(_controller.Capture.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                SourceBox.SelectedItem = choice;
        }
        foreach (var window in _controller.Windows().Take(40))
        {
            var choice = new SourceChoice(window.Title + "  ·  " + window.ProcessName, null, window);
            SourceBox.Items.Add(choice);
            if (_controller.Capture.Kind == CaptureKind.Window && _controller.Capture.Handle == window.Handle)
                SourceBox.SelectedItem = choice;
        }
        if (SourceBox.SelectedIndex < 0 && SourceBox.Items.Count > 0)
            SourceBox.SelectedIndex = 0;
    }

    void SourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SourceBox.SelectedItem is not SourceChoice choice)
            return;
        if (choice.Display != null)
            _controller.SelectMonitor(choice.Display);
        else if (choice.Window != null)
            _controller.SelectWindow(choice.Window);
    }

    sealed record SourceChoice(string Label, DisplayEntry? Display, WindowEntry? Window)
    {
        public override string ToString() => Label;
    }

    void FpsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FpsBox.SelectedItem is not string text || !int.TryParse(text, out var fps))
            return;
        _controller.Settings.FrameRate = fps;
        _controller.Settings.Profile = QualityProfile.Custom;
        _controller.Save();
        Reload();
    }

    void SoftwareChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _controller.Settings.AllowSoftwareEncoder = SoftwareBox.IsChecked == true;
        _controller.Save();
    }

    void CursorChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _controller.Settings.CaptureCursor = CursorBox.IsChecked == true;
        _controller.Save();
    }

    void HotkeyFocused(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => HotkeyEditingChanged?.Invoke(true);

    void HotkeyUnfocused(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.NewFocus, ReplayHotkey) || ReferenceEquals(e.NewFocus, RecordHotkey)) return;
        HotkeyEditingChanged?.Invoke(false);
    }

    void CaptureReplayHotkey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        if (e.IsRepeat) return;
        if (!HotkeyText.TryFromKey(e, out var binding))
            return;
        _controller.Settings.ReplayHotkey = binding;
        _controller.Save();
        if (Window.GetWindow(this) is MainWindow window)
            window.RebindHotkeys();
        Reload();
    }

    void CaptureRecordHotkey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        if (e.IsRepeat) return;
        if (!HotkeyText.TryFromKey(e, out var binding))
            return;
        _controller.Settings.RecordHotkey = binding;
        _controller.Save();
        if (Window.GetWindow(this) is MainWindow window)
            window.RebindHotkeys();
        Reload();
    }

    void AudioToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not System.Windows.Controls.CheckBox box || box.Tag is not AudioDeviceEntry device)
            return;
        if (device.IsMicrophone)
        {
            _controller.Settings.RecordMicrophone = box.IsChecked == true;
            _controller.Settings.MicrophoneId = device.Id;
        }
        else
        {
            var ids = _controller.Settings.OutputDeviceIds;
            if (ids.Count == 0)
                ids.AddRange(_controller.AudioDevices().Where(d => !d.IsMicrophone && d.IsDefault).Select(d => d.Id));
            if (box.IsChecked == true)
            {
                if (!ids.Contains(device.Id))
                    ids.Add(device.Id);
            }
            else
            {
                ids.RemoveAll(id => id.Equals(device.Id, StringComparison.OrdinalIgnoreCase));
                if (ids.Count == 0)
                    _controller.Settings.CaptureSystemAudio = false;
            }
        }
        _controller.Save();
        Reload();
    }

    void SystemAudioChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var testing = _controller.Snapshot().AudioTestActive;
        if (testing) _controller.StopAudioTest();
        _controller.Settings.CaptureSystemAudio = SystemAudioBox.IsChecked == true;
        _controller.Save();
        BuildAudio();
        AudioModeBox.IsEnabled = _controller.Settings.CaptureSystemAudio != false;
        BuildApplications();
        if (testing) _controller.StartAudioTest();
    }

    void ReplayDurationChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
    {
        if (_loading) return;
        _controller.Settings.ReplaySeconds = ReplayDuration.Seconds;
        _controller.Save();
    }

    void AudioModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AudioModeBox.SelectedIndex < 0) return;
        var testing = _controller.Snapshot().AudioTestActive;
        if (testing) _controller.StopAudioTest();
        _controller.Settings.AudioMode = AudioModeBox.SelectedIndex == 1 ? AudioCaptureMode.Applications : AudioCaptureMode.System;
        _controller.Save();
        BuildAudio();
        BuildApplications();
        if (testing) _controller.StartAudioTest();
    }

    void RefreshAudioApplications(object sender, RoutedEventArgs e) => BuildApplications();

    void BuildApplications()
    {
        ApplicationAudioPanel.Visibility = _controller.Settings.AudioMode == AudioCaptureMode.Applications ? Visibility.Visible : Visibility.Collapsed;
        ApplicationAudioList.Children.Clear();
        if (ApplicationAudioPanel.Visibility != Visibility.Visible) return;
        var enabled = _controller.Settings.CaptureSystemAudio != false && _controller.ApplicationAudioSupported;
        foreach (var app in _controller.AudioApplications())
        {
            var row = new Grid();
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock { Text = app.Name, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            var status = new TextBlock { FontSize = 10, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap,
                Text = app.Id == AudioApplicationEntry.GameId ? "Follows your game, even when you switch back to Spezi."
                    : app.IsRunning ? "App is running" : "Capture starts when the app is running." };
            status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            heading.Children.Add(status);
            var toggle = new System.Windows.Controls.CheckBox { Content = heading, Tag = app,
                IsChecked = _controller.Settings.AudioApplicationIds.Contains(app.Id, StringComparer.OrdinalIgnoreCase), IsEnabled = enabled };
            toggle.Checked += ApplicationAudioToggled;
            toggle.Unchecked += ApplicationAudioToggled;
            Grid.SetColumnSpan(toggle, 2);
            row.Children.Add(toggle);
            var slider = new Slider { Minimum = 0, Maximum = 200, Value = _controller.Settings.GainFor(app.Id) * 100, Tag = app.Id,
                IsEnabled = enabled, Margin = new Thickness(10, 7, 12, 0), ToolTip = "Recording volume" };
            slider.ValueChanged += GainChanged;
            Grid.SetRow(slider, 1);
            row.Children.Add(slider);
            var meter = new System.Windows.Controls.ProgressBar { Height = 8, Maximum = 1, Tag = app.Id, Margin = new Thickness(0, 7, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            meter.SetResourceReference(System.Windows.Controls.ProgressBar.ForegroundProperty, "Accent");
            Grid.SetRow(meter, 1); Grid.SetColumn(meter, 1);
            row.Children.Add(meter);
            var wrapper = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Child = row };
            wrapper.SetResourceReference(Border.BackgroundProperty, "Field");
            ApplicationAudioList.Children.Add(wrapper);
        }
        UpdateApplicationHint();
    }

    void ApplicationAudioToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not System.Windows.Controls.CheckBox { Tag: AudioApplicationEntry app } toggle) return;
        var testing = _controller.Snapshot().AudioTestActive;
        if (testing) _controller.StopAudioTest();
        var ids = _controller.Settings.AudioApplicationIds;
        if (toggle.IsChecked == true) { if (!ids.Contains(app.Id, StringComparer.OrdinalIgnoreCase)) ids.Add(app.Id); }
        else ids.RemoveAll(id => id.Equals(app.Id, StringComparison.OrdinalIgnoreCase));
        _controller.Save();
        UpdateApplicationHint();
        if (testing) _controller.StartAudioTest();
    }

    void UpdateApplicationHint() => ApplicationSelectionHint.Text = _controller.Settings.AudioApplicationIds.Count == 0
        ? "No apps selected: app audio will not be recorded."
        : "Only selected apps are recorded. Other apps and Windows notifications are excluded.";

    void GainChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || sender is not Slider slider || slider.Tag is not string id)
            return;
        _controller.Settings.DeviceGains[id] = slider.Value / 100d;
        _controller.Save();
    }

    void ToggleTest(object sender, RoutedEventArgs e)
    {
        if (_controller.Snapshot().AudioTestActive)
            _controller.StopAudioTest();
        else
            _controller.StartAudioTest();
    }

    void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _controller.Settings.StartWithWindows = StartupBox.IsChecked == true;
        _controller.Save();
    }

    void AutoChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _controller.Settings.AutoReplayWhenPlaying = AutoBox.IsChecked == true;
        _controller.Save();
    }

    void ToastChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        _controller.Settings.StatusToasts = ToastBox.IsChecked == true;
        _controller.Save();
    }

    void ToastDurationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _controller.Settings.ToastDurationSeconds = ToastDurationSlider.Value;
        _controller.Save();
        ToastDurationText.Text = $"{_controller.Settings.ToastDurationSeconds:0.#} seconds";
    }

    void ChooseAccent(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { CommandParameter: string color }) SetAccent(color);
    }

    void ChooseCustomAccent(object sender, RoutedEventArgs e)
    {
        var color = AppTheme.Parse(_controller.Settings.AccentColor);
        using var picker = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B) };
        if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SetAccent($"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}");
    }

    void SetAccent(string color)
    {
        _controller.Settings.AccentColor = color;
        _controller.Save();
        PaintAccentChoice();
    }

    void PaintAccentChoice()
    {
        var selected = _controller.Settings.AccentColor;
        AccentName.Text = "Custom color";
        foreach (var choice in AccentPalette.Children.OfType<System.Windows.Controls.Button>())
        {
            var active = string.Equals(choice.CommandParameter as string, selected, StringComparison.OrdinalIgnoreCase);
            choice.Tag = active ? "on" : null;
            if (active) AccentName.Text = choice.ToolTip?.ToString() ?? "Custom color";
        }
    }

    static string ProfileName(QualityProfile profile) => profile switch
    {
        QualityProfile.Economy => "Efficient",
        QualityProfile.Balanced => "Balanced",
        QualityProfile.High => "High quality",
        _ => "Custom"
    };
}
