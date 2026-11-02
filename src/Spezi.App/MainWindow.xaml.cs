using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Spezi.Core;

namespace Spezi.App;

public partial class MainWindow : Window
{
    readonly AppController _controller;
    readonly HotkeyHost _hotkeys = new();
    readonly TrayHost _tray = new();
    readonly ToastWindow _toast = new();
    readonly CapturePage _capture;
    readonly GalleryPage _gallery;
    readonly SettingsPage _settings;
    bool _exiting;
    bool _toldAboutTray;
    bool _editingHotkey;

    public MainWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        _capture = new CapturePage(controller);
        _gallery = new GalleryPage(controller);
        _settings = new SettingsPage(controller);
        _settings.HotkeyEditingChanged += editing =>
        {
            if (editing && IsActive) BeginHotkeyEdit();
            else EndHotkeyEdit();
        };
        Activated += (_, _) => { if (_settings.IsVisible && _settings.IsHotkeyEditing) BeginHotkeyEdit(); };
        Deactivated += (_, _) => EndHotkeyEdit();
        _controller.Notice += OnNotice;
        _controller.LibraryChanged += () => Dispatcher.BeginInvoke(() =>
        {
            _capture.RefreshClips();
            if (Body.Content == _gallery) _gallery.RefreshList();
        });
        _controller.StateChanged += () => Dispatcher.BeginInvoke(RefreshNav);
        _hotkeys.Pressed += id => Dispatcher.BeginInvoke(() => _controller.HandleHotkey(id));
        _tray.OpenRequested += () => Dispatcher.BeginInvoke(BringForward);
        _tray.ExitRequested += () => Dispatcher.BeginInvoke(ExitApp);
        _capture.OpenGallery += (_, _) => ShowGallery(this, new RoutedEventArgs());
        _capture.OpenSettings += (_, _) => ShowSettings(this, new RoutedEventArgs());
        StateChanged += (_, _) =>
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        };
        _controller.BindHotkeys(_hotkeys);
        ShowCapture(this, new RoutedEventArgs());
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        int dark = 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        int caption = 0x0016110E;
        int border = 0x0016110E;
        int text = 0x00F6F4F4;
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
    }

    public void BringForward()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void RebindHotkeys()
    {
        if (!_exiting) _controller.BindHotkeys(_hotkeys, !_editingHotkey);
    }

    void BeginHotkeyEdit()
    {
        if (_editingHotkey) return;
        _editingHotkey = true;
        RebindHotkeys();
    }

    void EndHotkeyEdit()
    {
        if (!_editingHotkey) return;
        _editingHotkey = false;
        RebindHotkeys();
    }

    public void Toast(string message, string? path = null)
    {
        var text = string.IsNullOrWhiteSpace(path) ? message : message + " " + path;
        _toast.ShowMessage(text, _controller.Settings);
    }

    void OnNotice(RecorderNotice notice)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var status = notice.Kind is NoticeKind.ReplayStarted or NoticeKind.ReplayStopped or NoticeKind.ClipSaved
                or NoticeKind.SessionStarted or NoticeKind.SessionStopped;
            if (notice.Kind is NoticeKind.ClipSaved or NoticeKind.SessionStopped or NoticeKind.RecoveredRecording)
                _capture.RefreshClips();
            if (status && !_controller.Settings.StatusToasts)
            {
                RefreshTray();
                return;
            }
            _toast.ShowNotice(notice, _controller.Capture, _controller.Settings);
            RefreshTray();
        });
    }

    void ShowCapture(object sender, RoutedEventArgs e)
    {
        Tabs.Visibility = Visibility.Visible;
        Body.Content = _capture;
        Mark(CaptureNav);
        _capture.Refresh();
        _capture.RefreshClips();
        RefreshTray();
    }

    void ShowGallery(object sender, RoutedEventArgs e)
    {
        Body.Content = _gallery;
        Mark(GalleryNav);
        _gallery.RefreshList();
    }

    void ShowSettings(object sender, RoutedEventArgs e)
    {
        Body.Content = _settings;
        Mark(SettingsNav);
        _settings.Reload();
    }

    void Mark(System.Windows.Controls.Button selected)
    {
        foreach (var button in new[] { CaptureNav, GalleryNav, SettingsNav })
            button.Tag = ReferenceEquals(button, selected) ? "on" : null;
    }

    void RefreshNav()
    {
        if (Body.Content == _capture)
            _capture.Refresh();
        RefreshTray();
    }

    void RefreshTray()
    {
        var snapshot = _controller.Snapshot();
        _tray.SetStatus(snapshot.SessionActive
            ? "Spezi · Recording"
            : snapshot.ReplayActive ? "Spezi · Replay" : "Spezi");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
            if (!_toldAboutTray)
            {
                _toldAboutTray = true;
                Toast("Spezi is still running in the system tray. Use the tray menu to exit.");
            }
            return;
        }
        _hotkeys.Dispose();
        _tray.Dispose();
        _toast.Close();
        base.OnClosing(e);
    }

    void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void ToggleMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void CloseWindow(object sender, RoutedEventArgs e) => Close();

    void ExitApp()
    {
        _exiting = true;
        System.Windows.Application.Current.Shutdown();
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
