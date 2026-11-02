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
                RefreshClips();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                SetPulse(false);
                _details?.Cancel();
            }
        };
    }

    public void Refresh() => RefreshStatus();

    void RefreshClock()
    {
        var snapshot = _controller.Snapshot();
        _filling = true;
        ReplayToggle.IsChecked = snapshot.ReplayActive;
        _filling = false;
        ApplyLive(snapshot);
        var hasBuffer = snapshot.ReplayActive && snapshot.ReplayBuffered >= TimeSpan.FromSeconds(1);
        if (ClipButton.IsEnabled != hasBuffer && string.IsNullOrWhiteSpace(_controller.ReplayHotkeyError))
            RefreshStatus();
    }

    System.Windows.Media.Animation.Storyboard? _pulse;

    // Status parts that change while the page is visible: replay state, buffer, timer, mic level.
    void ApplyLive(RecorderSnapshot snapshot)
    {
        var settings = _controller.Settings;
        StatusText.Text = snapshot.ReplayActive ? "Replay on" : "Replay off";
        StatusMark.Fill = (System.Windows.Media.Brush)FindResource(snapshot.ReplayActive ? "Accent" : "Muted");
        StatusSub.Text = snapshot.ReplayActive
            ? $"Buffered {Clock(snapshot.ReplayBuffered)} of {Clock(TimeSpan.FromSeconds(settings.ReplaySeconds))}. Save the moment any time."
            : "Turn on to keep your last moments ready to save.";
        BufferChipText.Text = Clock(TimeSpan.FromSeconds(settings.ReplaySeconds)) + " buffer";
        QualityChipText.Text = $"{settings.Height}p \u00B7 {settings.FrameRate} FPS";
        SetPulse(snapshot.ReplayActive);

        RecordLabel.Text = snapshot.SessionActive ? "Stop recording" : "Start recording";
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
        var duration = TimeSpan.FromSeconds(1.6);
        _pulse = new System.Windows.Media.Animation.Storyboard { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        void Add(DependencyObject target, string property, double from, double to)
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation(from, to, duration) { EasingFunction = ease };
            System.Windows.Media.Animation.Storyboard.SetTarget(animation, target);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            _pulse.Children.Add(animation);
        }
        Add(StatusPulse, "Opacity", 0.55, 0);
        Add(StatusPulse, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 0.45, 1);
        Add(StatusPulse, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 0.45, 1);
        // Low frame rate: a status pulse does not need 60 FPS.
        System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(_pulse, 24);
        _pulse.Begin(this, true);
    }

    static string Clock(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    public void RefreshStatus()
    {
        var snapshot = _controller.Snapshot();
        var settings = _controller.Settings;
        _filling = true;
        ReplayToggle.IsChecked = snapshot.ReplayActive;
        SystemBox.IsChecked = settings.CaptureSystemAudio != false;
        var mic = _controller.AudioDevices().FirstOrDefault(device => device.IsMicrophone);
        MicBox.IsEnabled = mic != null;
        MicBox.IsChecked = settings.RecordMicrophone && mic != null;
        _filling = false;

        ClipHotkeyText.Text = HotkeyText.Format(settings.ReplayHotkey);
        RecordHotkeyText.Text = HotkeyText.Format(settings.RecordHotkey);
        ApplyLive(snapshot);
        RecordButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, RecordLabel.Text);

        var hasBuffer = snapshot.ReplayActive && snapshot.ReplayBuffered >= TimeSpan.FromSeconds(1);
        ClipButton.IsEnabled = hasBuffer;
        ClipHint.Visibility = hasBuffer ? Visibility.Collapsed : Visibility.Visible;
        ClipHint.Text = snapshot.ReplayActive
            ? "Buffer is filling up"
            : "Turn on replay first";

        ClipGlow.Color = ((SolidColorBrush)FindResource("Accent")).Color;
        ClipGlow.Opacity = hasBuffer ? 0.55 : 0;
        var hotkeyError = _controller.HotkeyErrorText;
        HotkeyWarning.Visibility = hotkeyError.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        HotkeyWarningText.Text = hotkeyError;

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
                .Take(3)
                .Select(file => new RecentClip(file))
                .ToList();
        }, cancellation);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { AppLog.Error("Recent clip scan failed.", ex); return; }
        // Resumes on the UI thread.
            {
                if (generation != _generation || cancellation.IsCancellationRequested) return;
                _recent.Clear();
                foreach (var clip in result)
                    _recent.Add(clip);
                EmptyClips.Visibility = _recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (IsVisible)
                    _ = LoadClipsAsync(generation);
            }
        }
    }

    async Task LoadClipsAsync(int generation)
    {
        var cancellation = _details?.Token ?? CancellationToken.None;
        foreach (var clip in _recent.ToArray())
        {
            if (generation != _generation || cancellation.IsCancellationRequested)
                return;
            try
            {
                var details = await ClipCache.GetDetailsAsync(clip.Path, cancellation);
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
