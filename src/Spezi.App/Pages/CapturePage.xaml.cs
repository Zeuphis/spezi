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
                _details?.Cancel();
            }
        };
    }

    public void Refresh() => RefreshStatus();

    void RefreshClock()
    {
        var snapshot = _controller.Snapshot();
        StatusText.Text = snapshot.ReplayActive ? "Replay on" : "Replay off";
        StatusMark.Fill = (System.Windows.Media.Brush)FindResource(snapshot.ReplayActive ? "Accent" : "Muted");
        _filling = true;
        ReplayToggle.IsChecked = snapshot.ReplayActive;
        _filling = false;
        RecordLabel.Text = snapshot.SessionActive ? "Stop recording" : "Start recording";
        if (snapshot.SessionActive)
        {
            RecordTime.Visibility = Visibility.Visible;
            RecordTime.Text = snapshot.SessionElapsed.TotalHours >= 1
                ? snapshot.SessionElapsed.ToString(@"h\:mm\:ss")
                : snapshot.SessionElapsed.ToString(@"mm\:ss");
        }
        else
        {
            RecordTime.Visibility = Visibility.Collapsed;
        }
        var hasBuffer = snapshot.ReplayActive && snapshot.ReplayBuffered >= TimeSpan.FromSeconds(1);
        if (ClipButton.IsEnabled != hasBuffer && string.IsNullOrWhiteSpace(_controller.ReplayHotkeyError))
            RefreshStatus();
    }

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

        StatusText.Text = snapshot.ReplayActive ? "Replay on" : "Replay off";
        StatusMark.Fill = (System.Windows.Media.Brush)FindResource(snapshot.ReplayActive ? "Accent" : "Muted");
        ClipHotkeyText.Text = HotkeyText.Format(settings.ReplayHotkey);
        RecordHotkeyText.Text = HotkeyText.Format(settings.RecordHotkey);
        RecordLabel.Text = snapshot.SessionActive ? "Stop recording" : "Start recording";
        RecordButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, RecordLabel.Text);
        if (snapshot.SessionActive)
        {
            RecordTime.Visibility = Visibility.Visible;
            RecordTime.Text = snapshot.SessionElapsed.TotalHours >= 1
                ? snapshot.SessionElapsed.ToString(@"h\:mm\:ss")
                : snapshot.SessionElapsed.ToString(@"mm\:ss");
        }
        else
        {
            RecordTime.Visibility = Visibility.Collapsed;
        }

        var hasBuffer = snapshot.ReplayActive && snapshot.ReplayBuffered >= TimeSpan.FromSeconds(1);
        ClipButton.IsEnabled = hasBuffer;
        ClipHint.Visibility = hasBuffer ? Visibility.Collapsed : Visibility.Visible;
        ClipHint.Text = snapshot.ReplayActive
            ? "The buffer is still empty. You can save a clip once frames are available."
            : "Enable replay to start buffering clips.";

        var hotkeyError = _controller.HotkeyErrorText;
        if (hotkeyError.Length > 0)
        {
            ClipHint.Visibility = Visibility.Visible;
            ClipHint.Text = hotkeyError;
        }

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
