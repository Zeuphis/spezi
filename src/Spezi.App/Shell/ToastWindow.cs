using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Spezi.Core;
using Screen = System.Windows.Forms.Screen;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace Spezi.App;

sealed class ToastWindow : Window
{
    readonly TextBlock _title;
    readonly TextBlock _detail;
    readonly TextBlock _file;
    readonly TextBlock _glyph;
    readonly TextBlock _badge;
    readonly Border _icon;
    readonly Border _accent;
    readonly DispatcherTimer _timer;
    const double EdgeGap = 20;
    internal static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(3.5);

    public ToastWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = 380;
        MinHeight = 78;
        SizeToContent = SizeToContent.Height;

        var layout = new Grid { Margin = new Thickness(14, 10, 14, 10) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid();
        header.Children.Add(Text("SPEZI", 10, "#AAB6BE", FontWeights.Bold));
        _badge = Text("INFO", 9, "#5CDBA6", FontWeights.SemiBold);
        _badge.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        header.Children.Add(_badge);
        layout.Children.Add(header);

        var body = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(11) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _glyph = Text("\uE73E", 17, "#5CDBA6");
        _glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
        _glyph.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        _glyph.VerticalAlignment = VerticalAlignment.Center;
        _icon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), Background = Brush("#1F3C33"), Child = _glyph, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(_icon);
        var copy = new StackPanel();
        _title = Text("Spezi", 16, "#F4F7F8", FontWeights.SemiBold);
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        copy.Children.Add(_title);
        _detail = Text("", 11.5, "#B0BAC3");
        _detail.Margin = new Thickness(0, 3, 0, 0);
        _detail.TextWrapping = TextWrapping.Wrap;
        _detail.TextTrimming = TextTrimming.CharacterEllipsis;
        _detail.LineHeight = 17;
        _detail.MaxHeight = 34;
        copy.Children.Add(_detail);
        _file = Text("", 11, "#7E8E9B");
        _file.Margin = new Thickness(0, 3, 0, 0);
        _file.TextTrimming = TextTrimming.CharacterEllipsis;
        copy.Children.Add(_file);
        Grid.SetColumn(copy, 2);
        body.Children.Add(copy);
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        var layers = new Grid();
        layers.Children.Add(layout);
        _accent = new Border { Height = 2, Background = Brush("#5CDBA6"), CornerRadius = new CornerRadius(1), Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.55 };
        layers.Children.Add(_accent);
        Content = new Border { CornerRadius = new CornerRadius(12), Background = Brush("#23292F"), BorderBrush = Brush("#3B464E"), BorderThickness = new Thickness(1), Child = layers };
        _timer = new DispatcherTimer { Interval = DisplayDuration };
        _timer.Tick += (_, _) => { _timer.Stop(); Hide(); };
        Closed += (_, _) => _timer.Stop();
    }

    static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    static TextBlock Text(string text, double size, string color, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = Brush(color), FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.NoWrap
    };

    public void ShowMessage(string message, AppSettings? settings = null) => ShowNotice(new RecorderNotice { Kind = NoticeKind.Information, Message = message }, settings: settings);

    public void ShowNotice(RecorderNotice notice, CaptureSelection? capture = null, AppSettings? settings = null)
    {
        var warning = notice.Kind is NoticeKind.AudioDeviceLost or NoticeKind.CaptureSourceLost or NoticeKind.DiskLow;
        var error = notice.Kind is NoticeKind.Error or NoticeKind.DiskFull or NoticeKind.WriteAccessDenied or NoticeKind.EncoderMissing;
        var saved = notice.Kind is NoticeKind.ClipSaved or NoticeKind.SessionStopped or NoticeKind.RecoveredRecording;
        var replay = notice.Kind == NoticeKind.ReplayStarted;
        var recording = notice.Kind == NoticeKind.SessionStarted;
        var color = error ? "#F58B82" : warning ? "#F2C57C" : AppSettings.ValidAccentColor(settings?.AccentColor);
        _title.Text = notice.Kind switch
        {
            NoticeKind.ReplayStarted => "Replay on",
            NoticeKind.ReplayStopped => "Replay off",
            NoticeKind.ClipSaved => notice.Message.TrimEnd('.'),
            NoticeKind.SessionStarted => "Recording started",
            NoticeKind.SessionStopped => "Recording saved",
            NoticeKind.RecoveredRecording => "Recording recovered",
            _ => error ? "Something went wrong" : warning ? "Please check" : "Spezi"
        };
        _detail.Text = saved || recording || notice.Kind == NoticeKind.ReplayStopped ? ""
            : replay ? notice.Message.Replace("Replay is on. ", "", StringComparison.Ordinal)
            : notice.Message;
        _detail.Visibility = _detail.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _file.Text = string.IsNullOrWhiteSpace(notice.FilePath) ? "" : Path.GetFileName(notice.FilePath);
        _file.Visibility = _file.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _badge.Text = error ? "ERROR" : warning ? "NOTICE" : saved ? "SAVED" : replay || recording ? "ACTIVE" : "INFO";
        _badge.Foreground = _glyph.Foreground = error || warning ? Brush(color) : AppTheme.Text(color);
        _accent.Background = Brush(color);
        _icon.Background = error || warning ? Brush(error ? "#442C2E" : "#413727") : AppTheme.Soft(color);
        _glyph.Text = error || warning ? "\uE7BA" : saved ? "\uE73E" : replay ? "\uE72C" : recording ? "\uE714" : "\uE946";

        // Select the monitor before showing a topmost window. The foreground game keeps focus.
        var screen = TargetScreen(capture);
        _timer.Stop();
        Opacity = 0;
        if (!IsVisible) Show();
        var handle = new WindowInteropHelper(this).Handle;
        // A destroyed or never-created handle would leave the toast invisible; let the caller recreate it.
        if (handle == 0 || !IsWindow(handle)) throw new InvalidOperationException("Toast window handle is not available.");
        Position(screen);
        Opacity = 1;
        _timer.Interval = TimeSpan.FromSeconds(AppSettings.ValidToastDuration(settings?.ToastDurationSeconds ?? DisplayDuration.TotalSeconds));
        _timer.Start();
    }

    static Screen TargetScreen(CaptureSelection? capture)
    {
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var processId);
        if (foreground != 0 && processId != Environment.ProcessId)
            return Screen.FromHandle(foreground);
        if (capture?.Kind == CaptureKind.Window && capture.Handle != 0 && IsWindow(capture.Handle))
            return Screen.FromHandle(capture.Handle);
        return Screen.AllScreens.FirstOrDefault(screen => string.Equals(screen.DeviceName, capture?.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
            ?? (foreground != 0 ? Screen.FromHandle(foreground) : Screen.PrimaryScreen ?? Screen.AllScreens[0]);
    }

    void Position(Screen screen)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var area = screen.WorkingArea;
        // Move in physical pixels first so WPF adopts the target monitor's DPI, including negative coordinates.
        SetWindowPos(handle, new IntPtr(-1), area.Left, area.Top, 0, 0, 0x0011);
        UpdateLayout();
        var scale = GetDpiForWindow(handle) / 96d;
        if (scale <= 0) scale = 1;
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var gap = (int)Math.Round(EdgeGap * scale);
        // SWP_NOACTIVATE | SWP_SHOWWINDOW: re-assert topmost and visibility on every toast.
        SetWindowPos(handle, new IntPtr(-1), area.Right - width - gap, area.Top + gap, width, height, 0x0050);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        // No activation, no taskbar entry and no mouse input intercepted from a game.
        SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000L | 0x00000080L | 0x00000020L));
    }

    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
