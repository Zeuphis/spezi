using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Spezi.Core;
using Spezi.Core.Recording;

namespace Spezi.App;

public partial class CapturePage : System.Windows.Controls.UserControl
{
    readonly AppController _controller;
    readonly DispatcherTimer _timer;
    readonly ObservableCollection<RecentClip> _recent = [];
    bool _filling;
    int _generation;
    CancellationTokenSource? _details;

    public event EventHandler? OpenGallery;
    public event EventHandler? OpenSettings;

    public CapturePage(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        RecentList.ItemsSource = _recent;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => RefreshClock();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshStatus();
                RefreshMicAsync();
                RefreshClips();
            }
            else
            {
                _details?.Cancel();
            }
            UpdateActivity();
        };
        Loaded += (_, _) => { _host = Window.GetWindow(this); if (_host != null) _host.StateChanged += HostStateChanged; UpdateActivity(); };
        Unloaded += (_, _) => { if (_host != null) _host.StateChanged -= HostStateChanged; _host = null; UpdateActivity(); };
    }

    Window? _host;
    void HostStateChanged(object? sender, EventArgs e) { if (Active) RefreshStatus(); UpdateActivity(); }
    // Hidden page, tray or minimized window: no timer ticks and no pulse animation.
    bool Active => IsVisible && _host?.WindowState != WindowState.Minimized;
    void UpdateActivity()
    {
        if (Active) { if (!_timer.IsEnabled) _timer.Start(); }
        else { _timer.Stop(); SetPulse(false); }
    }

    public void Refresh() => RefreshStatus();

    void RefreshClock()
    {
        var snapshot = _controller.Snapshot();
        _filling = true;
        ReplayToggle.IsChecked = ReplayOn(snapshot);
        _filling = false;
        ApplyLive(snapshot);
        var hasBuffer = ReplayOn(snapshot) && Buffered(snapshot) >= TimeSpan.FromSeconds(1);
        if (ClipButton.IsEnabled != hasBuffer && string.IsNullOrWhiteSpace(_controller.ReplayHotkeyError))
            RefreshStatus();
    }

    System.Windows.Media.Animation.Storyboard? _pulse;

    // Status parts that change while the page is visible: replay state, buffer, timer, mic level.
    void ApplyLive(RecorderSnapshot snapshot)
    {
        var settings = _controller.Settings;
        var on = ReplayOn(snapshot);
        var full = TimeSpan.FromSeconds(settings.ReplaySeconds);
        StatusText.Text = on ? "Replay on" : "Replay off";
        var accent = (System.Windows.Media.Brush)FindResource("Accent");
        StatusMark.Stroke = on ? accent : (System.Windows.Media.Brush)FindResource("Muted");
        PowerIcon.Stroke = on ? (System.Windows.Media.Brush)FindResource("Ink") : (System.Windows.Media.Brush)FindResource("Muted");
        HeroTint.Opacity = on ? 1 : 0;
        HeroCard.BorderBrush = on ? accent : (System.Windows.Media.Brush)FindResource("Line");
        HeroGlow.Color = ((SolidColorBrush)accent).Color;
        HeroGlow.Opacity = on ? 0.45 : 0;
        // Static text only: no buffer-fill indication.
        var subtitle = on ? $"Last {Clock(full)} ready to save" : "Press to keep your last moments";
        if (StatusSub.Text != subtitle) StatusSub.Text = subtitle;
        BufferChipText.Text = Clock(full) + " buffer";
        QualityChipText.Text = $"{settings.Height}p \u00B7 {settings.FrameRate} FPS";
        SetPulse(on && Active);

        RecordLabel.Text = snapshot.SessionActive ? "Stop" : "Record";
        RecordButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, snapshot.SessionActive ? "Stop recording" : "Start recording");
        RecordTime.Visibility = snapshot.SessionActive ? Visibility.Visible : Visibility.Collapsed;
        if (snapshot.SessionActive) RecordTime.Text = Clock(snapshot.SessionElapsed);
        RecordButton.Background = snapshot.SessionActive ? (System.Windows.Media.Brush)FindResource("Record") : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x1F, 0x23));
        RecordDot.Fill = snapshot.SessionActive ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("Record");

        var mic = snapshot.Levels.Where(level => level.IsMicrophone).Select(level => (double)level.Peak).DefaultIfEmpty(0).Max();
        MicLevel.Value = Math.Min(1, mic);
        MicLevel.Visibility = settings.RecordMicrophone ? Visibility.Visible : Visibility.Hidden;
    }

    void SetPulse(bool on)
    {
        if (on == (_pulse != null)) return;
        if (!on) { _pulse!.Stop(); _pulse = null; StatusPulse.Opacity = 0; return; }
        var ease = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var duration = TimeSpan.FromSeconds(2.2);
        _pulse = new System.Windows.Media.Animation.Storyboard { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        void Add(DependencyObject target, string property, double from, double to)
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation(from, to, duration) { EasingFunction = ease };
            System.Windows.Media.Animation.Storyboard.SetTarget(animation, target);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            _pulse.Children.Add(animation);
        }
        Add(StatusPulse, "Opacity", 0.35, 0);
        Add(StatusPulse, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 0.85, 1.12);
        Add(StatusPulse, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 0.85, 1.12);
        // Low frame rate: a status pulse does not need 60 FPS.
        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(_pulse, 24);
        _pulse.Begin(this, true);
    }

    // Screenshot self-test only: renders the "replay on" look without starting capture.
    bool _previewOn;
    internal void PreviewReplayOn(bool on = true) { _previewOn = on; RefreshStatus(); }
    bool ReplayOn(RecorderSnapshot snapshot) => snapshot.ReplayActive || _previewOn;
    TimeSpan Buffered(RecorderSnapshot snapshot) => _previewOn ? TimeSpan.FromSeconds(_controller.Settings.ReplaySeconds) : snapshot.ReplayBuffered;

    static string Clock(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    public void RefreshStatus()
    {
        var snapshot = _controller.Snapshot();
        var settings = _controller.Settings;
        _filling = true;
        ReplayToggle.IsChecked = ReplayOn(snapshot);
        SystemBox.IsChecked = settings.CaptureSystemAudio != false;
        // Cached: device enumeration is slow (COM/MMDevice) and runs off the UI thread in RefreshMicAsync.
        MicBox.IsEnabled = _micAvailable;
        MicBox.IsChecked = settings.RecordMicrophone && _micAvailable;
        _filling = false;

        ClipHotkeyText.Text = HotkeyText.Format(settings.ReplayHotkey);
        RecordHotkeyText.Text = HotkeyText.Format(settings.RecordHotkey);
        ApplyLive(snapshot);

        var hasBuffer = ReplayOn(snapshot) && Buffered(snapshot) >= TimeSpan.FromSeconds(1);
        ClipButton.IsEnabled = hasBuffer;
        ClipHint.Visibility = hasBuffer ? Visibility.Collapsed : Visibility.Visible;
        ClipHint.Text = snapshot.ReplayActive
            ? "Buffer is filling up"
            : "Turn on replay first";

        ClipGlow.Color = ((SolidColorBrush)FindResource("Accent")).Color;
        ClipGlow.Opacity = hasBuffer ? 0.35 : 0;
        var hotkeyError = _controller.HotkeyErrorText;
        HotkeyWarning.Visibility = hotkeyError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        HotkeyWarningText.Text = hotkeyError;

        var audioWarning = settings.CaptureSystemAudio != false
            ? settings.AudioMode != AudioCaptureMode.System && settings.AudioApplicationIds.Count == 0
            : !settings.RecordMicrophone;
        AudioHint.Visibility = audioWarning ? Visibility.Visible : Visibility.Collapsed;
        AudioHint.Text = settings.CaptureSystemAudio != false
            ? settings.AudioMode == AudioCaptureMode.System ? "All system audio is included."
                : settings.AudioApplicationIds.Count == 0 ? "No apps selected. Choose your audio sources."
                : "Only selected apps are included."
            : settings.RecordMicrophone
                ? "Only the microphone is included."
                : "Audio is off. Enable app audio or your microphone.";

        FolderText.Text = ShortFolder(settings.OutputDirectory);
        FolderButton.ToolTip = settings.OutputDirectory;
        var free = _controller.FreeBytes();
        FreeText.Text = free <= 0 ? "Storage unavailable" : free >= 10L * 1024 * 1024 * 1024
            ? $"{free / 1024d / 1024 / 1024:0} GB free"
            : $"{free / 1024d / 1024 / 1024:0.0} GB free";
    }

    bool _micAvailable = true;
    int _micGeneration;
    async void RefreshMicAsync()
    {
        var generation = ++_micGeneration;
        bool available;
        try { available = await Task.Run(() => _controller.AudioDevices().Any(device => device.IsMicrophone)); }
        catch (Exception ex) { AppLog.Error("Microphone lookup failed.", ex); return; }
        if (generation != _micGeneration || available == _micAvailable) return;
        _micAvailable = available;
        _filling = true;
        MicBox.IsEnabled = available;
        MicBox.IsChecked = _controller.Settings.RecordMicrophone && available;
        _filling = false;
    }

    public void RefreshClips()
    {
        var generation = ++_generation;
        _details?.Cancel();
        _details?.Dispose();
        _details = new CancellationTokenSource();
        var cancellation = _details.Token;
        var directory = _controller.Settings.OutputDirectory;
        _ = PopulateAsync();
        async Task PopulateAsync()
        {
        List<RecentClip> result;
        try
        {
        result = await Task.Run(() =>
        {
            if (!Directory.Exists(directory))
                return new List<RecentClip>();
            return ClipLibrary.VideoFiles(directory)
                .Select(path => { cancellation.ThrowIfCancellationRequested(); return new FileInfo(path); })
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(8)
                .Select(file => new RecentClip(file))
                .ToList();
        }, cancellation);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { AppLog.Error("Recent clip scan failed.", ex); return; }
        // Resumes on the UI thread.
            {
                if (generation != _generation || cancellation.IsCancellationRequested) return;
                // Same clips as last time: keep the existing tiles and their thumbnails (no flicker, no re-decode).
                if (!result.Select(clip => clip.Path).SequenceEqual(_recent.Select(clip => clip.Path), StringComparer.OrdinalIgnoreCase))
                {
                    var previous = _recent.ToDictionary(clip => clip.Path, StringComparer.OrdinalIgnoreCase);
                    _recent.Clear();
                    foreach (var clip in result)
                        _recent.Add(previous.TryGetValue(clip.Path, out var kept) ? kept : clip);
                }
                EmptyClips.Visibility = _recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (IsVisible)
                    _ = LoadClipsAsync(generation);
            }
        }
    }

    async Task LoadClipsAsync(int generation)
    {
        var cancellation = _details?.Token ?? CancellationToken.None;
        // Same decode width as gallery tiles, so both pages share ClipCache's in-memory entries.
        var width = (int)Math.Ceiling(200 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        foreach (var clip in _recent.ToArray())
        {
            if (generation != _generation || cancellation.IsCancellationRequested)
                return;
            if (clip.Thumbnail != null) continue;
            try
            {
                var details = await ClipCache.GetDetailsAsync(clip.Path, width, cancellation);
                if (generation != _generation || cancellation.IsCancellationRequested) return;
                clip.DurationText = ClipText.FormatDuration(details.Duration);
                clip.Thumbnail = details.Thumbnail;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { AppLog.Error("Could not load recent clip: " + clip.Path, ex); }
        }
    }

    void ReplayToggled(object sender, RoutedEventArgs e)
    {
        if (_filling)
            return;
        if (ReplayToggle.IsChecked == true)
            _controller.StartReplay();
        else
            _controller.StopReplay();
        RefreshStatus();
    }

    async void SaveClip(object sender, RoutedEventArgs e)
    {
        ClipButton.IsEnabled = false;
        try
        {
            await _controller.SaveClip();
        }
        catch (Exception ex)
        {
            // async void: an escaping exception would terminate the app.
            AppLog.Error("Saving the clip failed.", ex);
        }
        finally
        {
            RefreshStatus();
            RefreshClips();
        }
    }

    void ToggleRecord(object sender, RoutedEventArgs e)
    {
        _controller.ToggleSession();
        RefreshStatus();
    }

    void SystemToggled(object sender, RoutedEventArgs e)
    {
        if (_filling)
            return;
        _controller.Settings.CaptureSystemAudio = SystemBox.IsChecked == true;
        _controller.Save();
        RefreshStatus();
    }

    void MicToggled(object sender, RoutedEventArgs e)
    {
        if (_filling)
            return;
        var enabled = MicBox.IsChecked == true;
        _controller.Settings.RecordMicrophone = enabled;
        if (enabled && string.IsNullOrWhiteSpace(_controller.Settings.MicrophoneId))
        {
            var mic = _controller.AudioDevices().FirstOrDefault(device => device.IsMicrophone && device.IsDefault)
                      ?? _controller.AudioDevices().FirstOrDefault(device => device.IsMicrophone);
            _controller.Settings.MicrophoneId = mic?.Id;
        }
        _controller.Save();
        RefreshStatus();
    }

    void ShowAll(object sender, RoutedEventArgs e) => OpenGallery?.Invoke(this, EventArgs.Empty);

    void OpenSettingsClick(object sender, RoutedEventArgs e) => OpenSettings?.Invoke(this, EventArgs.Empty);

    void PlayRecent(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecentClip { Path.Length: > 0 } clip)
            VideoLauncher.Open(clip.Path, Window.GetWindow(this));
    }

    void OpenFolder(object sender, RoutedEventArgs e)
    {
        var path = _controller.Settings.OutputDirectory;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    static string ShortFolder(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(full);
        var parent = Path.GetFileName(Path.GetDirectoryName(full) ?? "");
        if (string.IsNullOrEmpty(name))
            return path;
        return string.IsNullOrEmpty(parent) ? name : parent + " / " + name;
    }

    public sealed class RecentClip : INotifyPropertyChanged
    {
        public string Path { get; }
        public string Name { get; }
        string _duration = "…";
        ImageSource? _thumbnail;

        public RecentClip(FileInfo file)
        {
            Path = file.FullName;
            Name = System.IO.Path.GetFileNameWithoutExtension(file.Name);
        }

        public string DurationText
        {
            get => _duration;
            set
            {
                _duration = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationText)));
            }
        }

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
