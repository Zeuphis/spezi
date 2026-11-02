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
    readonly Spezi.Core.ViewModels.NotificationSettingsViewModel _notifications;
    bool _loading = true;
    internal event Action<bool>? HotkeyEditingChanged;
    internal bool IsHotkeyEditing => ReplayHotkey.IsKeyboardFocusWithin || RecordHotkey.IsKeyboardFocusWithin;

    public SettingsPage(AppController controller)
    {
        InitializeComponent();
        MakeSectionsCollapsible();
        _controller = controller;
        VersionText.Text = "Spezi v" + AppVersionText();
        _notifications = new(() => _controller.Settings, _controller.Save);
        ToastBox.DataContext = ToastDurationText.DataContext = ToastDurationSlider.DataContext = UpdateCheckBox.DataContext = _notifications;
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

    // Every setting card becomes a collapsible section (header row + chevron). Always starts collapsed.
    readonly List<Action> _sectionToggles = new();
    internal void ToggleSection(int index) => _sectionToggles[index]();

    void MakeSectionsCollapsible()
    {
        var cardStyle = (Style)FindResource("SettingCard");
        if (SettingsScroll.Content is not StackPanel root) return;
        foreach (var card in root.Children.OfType<Border>().Where(b => ReferenceEquals(b.Style, cardStyle)).ToList())
        {
            if (card.Child is not StackPanel panel || panel.Children.Count < 2 || panel.Children[0] is not FrameworkElement header) continue;
            var gap = header.Margin.Bottom;
            panel.Children.RemoveAt(0);
            var body = new StackPanel { Margin = new Thickness(0, gap, 0, 0) };
            while (panel.Children.Count > 0) { var child = panel.Children[0]; panel.Children.RemoveAt(0); body.Children.Add(child); }
            header.Margin = new Thickness(0);
            var chevron = new TextBlock
            {
                Text = "\uE70D", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 2, 0)
            };
            var row = new Grid { Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Focusable = true };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(header);
            Grid.SetColumn(chevron, 1);
            row.Children.Add(chevron);
            var title = header is TextBlock t ? t.Text : FindTitle(header) ?? "Section";
            System.Windows.Automation.AutomationProperties.SetName(row, title + " (collapsed)");
            var host = new Border { ClipToBounds = true, Child = body, Visibility = Visibility.Collapsed };
            panel.Children.Add(row);
            panel.Children.Add(host);
            var expanded = false;
            void Toggle()
            {
                expanded = !expanded;
                System.Windows.Automation.AutomationProperties.SetName(row, title + (expanded ? " (expanded)" : " (collapsed)"));
                var duration = new Duration(TimeSpan.FromMilliseconds(180));
                var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                chevron.Text = expanded ? "\uE70E" : "\uE70D";
                chevron.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.3, 1, duration));
                double from;
                if (expanded)
                {
                    host.Visibility = Visibility.Visible;
                    host.Height = 0;
                    from = 0;
                }
                else from = host.ActualHeight;
                body.Measure(new System.Windows.Size(host.ActualWidth > 0 ? host.ActualWidth : panel.ActualWidth, double.PositiveInfinity));
                var to = expanded ? body.DesiredSize.Height : 0;
                var animation = new System.Windows.Media.Animation.DoubleAnimation(from, to, duration) { EasingFunction = ease };
                var opening = expanded;
                animation.Completed += (_, _) =>
                {
                    if (opening != expanded) return;
                    host.BeginAnimation(HeightProperty, null);
                    host.Height = double.NaN;
                    if (!opening) host.Visibility = Visibility.Collapsed;
                };
                host.BeginAnimation(HeightProperty, animation);
            }
            _sectionToggles.Add(Toggle);
            row.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d && IsInsideButton(d, row)) return; Toggle(); };
            row.KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) { Toggle(); e.Handled = true; } };
        }
    }

    static bool IsInsideButton(DependencyObject source, DependencyObject stop)
    {
        for (var d = source; d != null && d != stop; d = VisualTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    static string? FindTitle(DependencyObject root)
    {
        if (root is TextBlock t && t.FontWeight.ToOpenTypeWeight() >= 600 && !string.IsNullOrWhiteSpace(t.Text)) return t.Text;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (FindTitle(child) is string found) return found;
        return null;
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
        MicBox.IsChecked = settings.RecordMicrophone;
        ReplayDuration.Seconds = settings.ReplaySeconds;
        ReplayMemoryText.Text = AppSettings.ReplayMemoryText(settings.ReplaySeconds, settings.VideoBitrateKbps);
        AudioModeBox.SelectedIndex = settings.AudioMode == AudioCaptureMode.Applications ? 1 : 0;
        AudioModeBox.IsEnabled = settings.CaptureSystemAudio != false;
        ApplicationsModeItem.IsEnabled = _controller.ApplicationAudioSupported;
        ApplicationAudioSupportText.Visibility = _controller.ApplicationAudioSupported ? Visibility.Collapsed : Visibility.Visible;
        ApplicationAudioSupportText.Text = "App-specific audio requires Windows 11. You can capture all system audio on this PC.";
        CursorBox.IsChecked = settings.CaptureCursor;
        ReplayHotkey.Text = HotkeyText.Format(settings.ReplayHotkey);
        RecordHotkey.Text = HotkeyText.Format(settings.RecordHotkey);
        HotkeyError.Text = _controller.HotkeyErrorText;
        StartupBox.IsChecked = settings.StartWithWindows;
        AutoBox.IsChecked = settings.AutoReplayWhenPlaying;
        _notifications.Load();
        PaintAccentChoice();
        var hardware = _controller.Encoders();
        EncoderText.Text = hardware.Count == 0
            ? "No hardware encoder detected. Enable software encoding below to record."
            : "Detected hardware encoder: " + string.Join(", ", hardware.Select(e => e.Name));
        if (hardware.Count == 0)
        {
            // Make the missing encoder obvious instead of a muted footnote.
            EncoderBox.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x2E, 0x1E));
            EncoderText.Foreground = System.Windows.Media.Brushes.Orange;
            EncoderText.Text = settings.AllowSoftwareEncoder
                ? "No hardware encoder detected. Spezi records with the software encoder, which uses much more CPU."
                : "No hardware encoder detected. Recording will not start until you enable software encoding under Advanced options.";
            AdvancedOptions.IsExpanded = true;
        }
        BuildAudio();
        BuildApplications();
        _loading = false;
        _controller.NoteDiskSpace();
    }

    void BuildAudio()
    {
        AudioList.Children.Clear();
        MicList.Children.Clear();
        var settings = _controller.Settings;
        var selected = settings.OutputDeviceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var device in _controller.AudioDevices().Where(d => d.IsMicrophone || settings.AudioMode == AudioCaptureMode.System))
        {
            var isChecked = device.IsMicrophone
                ? settings.RecordMicrophone && (settings.MicrophoneId == device.Id || settings.MicrophoneId == null && device.IsDefault)
                : selected.Count == 0 ? device.IsDefault : selected.Contains(device.Id);
            var enabled = device.IsMicrophone || settings.CaptureSystemAudio != false;
            (device.IsMicrophone ? MicList : AudioList).Children.Add(DeviceRow(device.Name + (device.IsDefault ? "  (default)" : ""), device, device.Id, isChecked, enabled, AudioToggled));
        }
    }

    // One compact row per source: checkbox + name, volume slider, level meter.
    Border DeviceRow(string name, object tag, string id, bool isChecked, bool enabled, RoutedEventHandler toggled, string? detail = null)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        var label = new StackPanel();
        label.Children.Add(new TextBlock { Text = name, FontSize = 11.5, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name });
        if (detail != null)
        {
            var status = new TextBlock { Text = detail, FontSize = 10, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            label.Children.Add(status);
        }
        var toggle = new System.Windows.Controls.CheckBox { Content = label, Tag = tag, IsChecked = isChecked, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch };
        toggle.Checked += toggled;
        toggle.Unchecked += toggled;
        var slider = new Slider { Minimum = 0, Maximum = 200, Value = _controller.Settings.GainFor(id) * 100, Tag = id, IsEnabled = enabled,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), ToolTip = "Recording volume" };
        slider.ValueChanged += GainChanged;
        Grid.SetColumn(slider, 1);
        var meter = new System.Windows.Controls.ProgressBar { Height = 6, Maximum = 1, Tag = id, VerticalAlignment = VerticalAlignment.Center };
        meter.SetResourceReference(System.Windows.Controls.ProgressBar.ForegroundProperty, "Accent");
        Grid.SetColumn(meter, 2);
        row.Children.Add(toggle);
        row.Children.Add(slider);
        row.Children.Add(meter);
        var wrapper = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 5), Child = row };
        wrapper.SetResourceReference(Border.BackgroundProperty, "Field");
        return wrapper;
    }

    void PaintLevels()
    {
        var levels = _controller.Snapshot().Levels;
        foreach (var row in AudioList.Children.OfType<Border>().Concat(MicList.Children.OfType<Border>()).Concat(ApplicationAudioList.Children.OfType<Border>()).Select(b => b.Child).OfType<Grid>())
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

    void MicChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var testing = _controller.Snapshot().AudioTestActive;
        if (testing) _controller.StopAudioTest();
        _controller.Settings.RecordMicrophone = MicBox.IsChecked == true;
        _controller.Save();
        Reload();
        if (testing) _controller.StartAudioTest();
    }

    void OpenLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Error("Could not open link", ex); }
        e.Handled = true;
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
        ReplayMemoryText.Text = AppSettings.ReplayMemoryText(ReplayDuration.Seconds, _controller.Settings.VideoBitrateKbps);
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
            var detail = app.Id == AudioApplicationEntry.GameId ? "Follows your game, even when you switch back to Spezi."
                : app.IsRunning ? "App is running" : "Capture starts when the app is running.";
            ApplicationAudioList.Children.Add(DeviceRow(app.Name, app, app.Id,
                _controller.Settings.AudioApplicationIds.Contains(app.Id, StringComparer.OrdinalIgnoreCase), enabled, ApplicationAudioToggled, detail));
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

    void ChooseAccent(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { CommandParameter: string color }) SetAccent(color);
    }

    void ChooseCustomAccent(object sender, RoutedEventArgs e)
    {
        var color = AppTheme.Parse(_controller.Settings.AccentColor);
        var picker = new ColorPickerWindow(color) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true)
            SetAccent(picker.SelectedHex);
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

    static string AppVersionText()
    {
        var info = System.Reflection.Assembly.GetEntryAssembly()?.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(info) ? typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "?" : info.Split('+')[0];
    }

    static string ProfileName(QualityProfile profile) => profile switch
    {
        QualityProfile.Economy => "Efficient",
        QualityProfile.Balanced => "Balanced",
        QualityProfile.High => "High quality",
        _ => "Custom"
    };
}
