using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using CharacterCasing = System.Windows.Controls.CharacterCasing;

namespace Spezi.App;

// Dark HSV color picker: hue/saturation wheel, H/S/B sliders, two-way hex field, old/new preview.
sealed class ColorPickerWindow : Window
{
    const int WheelSize = 220;
    static readonly WpfColor Purple = WpfColor.FromRgb(0xAC, 0x8B, 0xFA);
    readonly WpfColor _original;
    double _h, _s, _v;
    bool _updating;
    readonly System.Windows.Controls.Image _wheel = new() { Width = WheelSize, Height = WheelSize };
    readonly Ellipse _marker = new() { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false };
    readonly Canvas _wheelCanvas = new() { Width = WheelSize, Height = WheelSize, Background = Brushes.Transparent, Cursor = Cursors.Cross };
    readonly GradientSlider _hue, _sat, _val;
    readonly System.Windows.Controls.TextBox _hex = new();
    readonly TextBlock _hexError = new() { FontSize = 10.5, Foreground = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0x8A, 0x7A)), Margin = new Thickness(0, 4, 0, 0) };
    readonly Border _newSwatch = new() { Height = 34, CornerRadius = new CornerRadius(0, 8, 8, 0) };
    readonly System.Windows.Controls.Button _ok;

    public WpfColor SelectedColor { get; private set; }
    public string SelectedHex => $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";

    public ColorPickerWindow(WpfColor initial)
    {
        _original = initial; SelectedColor = initial;
        Title = "Custom color";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        (_h, _s, _v) = ToHsv(initial);

        _hue = new GradientSlider(360, v => { _h = v; Changed(); });
        _sat = new GradientSlider(100, v => { _s = v / 100; Changed(); });
        _val = new GradientSlider(100, v => { _v = v / 100; Changed(); });
        var hueStops = new GradientStopCollection();
        for (var i = 0; i <= 6; i++) hueStops.Add(new GradientStop(FromHsv(i * 60 % 360 + (i == 6 ? 359.9 : 0) - (i == 6 ? 0 : 0), 1, 1), i / 6d));
        _hue.Track.Background = new LinearGradientBrush(hueStops, 0);

        _wheelCanvas.Children.Add(_wheel);
        _wheelCanvas.Children.Add(_marker);
        _wheelCanvas.MouseLeftButtonDown += (_, e) => { _wheelCanvas.CaptureMouse(); PickWheel(e.GetPosition(_wheelCanvas)); };
        _wheelCanvas.MouseMove += (_, e) => { if (_wheelCanvas.IsMouseCaptured) PickWheel(e.GetPosition(_wheelCanvas)); };
        _wheelCanvas.MouseLeftButtonUp += (_, _) => _wheelCanvas.ReleaseMouseCapture();

        _hex.FontFamily = new System.Windows.Media.FontFamily("Consolas"); _hex.FontSize = 13; _hex.Padding = new Thickness(8, 6, 8, 6);
        _hex.MaxLength = 7; _hex.CharacterCasing = CharacterCasing.Upper;
        _hex.TextChanged += (_, _) => HexEdited();
        AutomationPropertiesName(_hex, "Hex color");

        var root = new StackPanel { Margin = new Thickness(18) };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = "Custom color", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Res("Ink") });
        var close = new System.Windows.Controls.Button { Content = "\uE8BB", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 10, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, ToolTip = "Cancel" };
        close.SetResourceReference(StyleProperty, "IconButton"); close.Width = 26; close.Height = 26;
        close.Click += (_, _) => DialogResult = false;
        header.Children.Add(close);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        body.Children.Add(_wheelCanvas);
        var side = new StackPanel(); Grid.SetColumn(side, 2);
        side.Children.Add(Labeled("HUE", _hue));
        side.Children.Add(Labeled("SATURATION", _sat));
        side.Children.Add(Labeled("BRIGHTNESS", _val));
        side.Children.Add(Label("HEX"));
        side.Children.Add(_hex);
        side.Children.Add(_hexError);
        side.Children.Add(Label("BEFORE / AFTER", 12));
        var preview = new Grid();
        preview.ColumnDefinitions.Add(new ColumnDefinition()); preview.ColumnDefinitions.Add(new ColumnDefinition());
        var oldSwatch = new Border { Height = 34, CornerRadius = new CornerRadius(8, 0, 0, 8), Background = new SolidColorBrush(initial), ToolTip = "Current color (click to restore)", Cursor = Cursors.Hand };
        oldSwatch.MouseLeftButtonUp += (_, _) => { (_h, _s, _v) = ToHsv(_original); Changed(); };
        Grid.SetColumn(_newSwatch, 1);
        preview.Children.Add(oldSwatch); preview.Children.Add(_newSwatch);
        side.Children.Add(new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = Res("Line"), Child = preview });
        body.Children.Add(side);
        root.Children.Add(body);

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        cancel.SetResourceReference(StyleProperty, "SmallButton");
        cancel.Click += (_, _) => DialogResult = false;
        _ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, MinWidth = 90, Background = new SolidColorBrush(Purple), BorderBrush = new SolidColorBrush(Purple), Foreground = new SolidColorBrush(WpfColor.FromRgb(0x16, 0x10, 0x2A)), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        _ok.Template = PillTemplate();
        _ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(_ok);
        root.Children.Add(buttons);

        var frame = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Child = root, Margin = new Thickness(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 } };
        frame.SetResourceReference(Border.BackgroundProperty, "Card");
        frame.SetResourceReference(Border.BorderBrushProperty, "Line");
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Content = frame;
        RenderWheel();
        Changed();
    }

    static void AutomationPropertiesName(DependencyObject o, string name) => System.Windows.Automation.AutomationProperties.SetName(o, name);
    WpfBrush Res(string key) => (WpfBrush)System.Windows.Application.Current.FindResource(key);
    TextBlock Label(string text, double top = 0) => new() { Text = text, FontSize = 10.5, Foreground = Res("Muted"), Margin = new Thickness(0, top, 0, 6) };
    StackPanel Labeled(string text, GradientSlider slider) { var p = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; p.Children.Add(Label(text)); p.Children.Add(slider); return p; }

    static ControlTemplate PillTemplate()
    {
        var t = new ControlTemplate(typeof(System.Windows.Controls.Button));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        bd.SetValue(Border.PaddingProperty, new Thickness(14, 7, 14, 7));
        bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88, "Bd"));
        var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "Bd"));
        t.Triggers.Add(hover); t.Triggers.Add(off);
        return t;
    }

    void PickWheel(System.Windows.Point p)
    {
        var r = WheelSize / 2d;
        var dx = p.X - r; var dy = p.Y - r;
        _h = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        _s = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / r);
        Changed();
    }

    void RenderWheel()
    {
        var bmp = new WriteableBitmap(WheelSize, WheelSize, 96, 96, PixelFormats.Bgra32, null);
        var px = new byte[WheelSize * WheelSize * 4];
        var r = WheelSize / 2d;
        for (var y = 0; y < WheelSize; y++)
        for (var x = 0; x < WheelSize; x++)
        {
            var dx = x + 0.5 - r; var dy = y + 0.5 - r; var d = Math.Sqrt(dx * dx + dy * dy);
            if (d > r) continue;
            var c = FromHsv((Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360, d / r, _v);
            var a = Math.Clamp(r - d, 0, 1); // 1px antialiased edge
            var i = (y * WheelSize + x) * 4;
            px[i] = (byte)(c.B * a); px[i + 1] = (byte)(c.G * a); px[i + 2] = (byte)(c.R * a); px[i + 3] = (byte)(255 * a);
        }
        bmp.WritePixels(new Int32Rect(0, 0, WheelSize, WheelSize), px, WheelSize * 4, 0);
        _wheel.Source = bmp;
    }

    double _renderedV = -1;
    void Changed(bool fromHex = false)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            SelectedColor = FromHsv(_h, _s, _v);
            if (Math.Abs(_renderedV - _v) > 0.004) { RenderWheel(); _renderedV = _v; }
            var r = WheelSize / 2d; var a = _h * Math.PI / 180;
            Canvas.SetLeft(_marker, r + Math.Cos(a) * _s * r - _marker.Width / 2);
            Canvas.SetTop(_marker, r + Math.Sin(a) * _s * r - _marker.Height / 2);
            _marker.Fill = new SolidColorBrush(SelectedColor);
            _hue.Value = _h; _sat.Value = _s * 100; _val.Value = _v * 100;
            _sat.Track.Background = new LinearGradientBrush(FromHsv(_h, 0, _v), FromHsv(_h, 1, _v), 0);
            _val.Track.Background = new LinearGradientBrush(Colors.Black, FromHsv(_h, _s, 1), 0);
            _newSwatch.Background = new SolidColorBrush(SelectedColor);
            if (!fromHex) { _hex.Text = SelectedHex; _hexError.Text = ""; _ok.IsEnabled = true; }
        }
        finally { _updating = false; }
    }

    void HexEdited()
    {
        if (_updating) return;
        var text = _hex.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length == 7 && int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            var c = WpfColor.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            (_h, _s, _v) = ToHsv(c);
            _hexError.Text = ""; _ok.IsEnabled = true;
            Changed(true);
            SelectedColor = c; // exact typed value, no HSV rounding
            _newSwatch.Background = new SolidColorBrush(c);
        }
        else
        {
            _hexError.Text = "Enter a color as #RRGGBB.";
            _ok.IsEnabled = false;
        }
    }

    internal static WpfColor FromHsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        var c = v * s; var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = v - c;
        var (r, g, b) = h < 60 ? (c, x, 0d) : h < 120 ? (x, c, 0d) : h < 180 ? (0d, c, x) : h < 240 ? (0d, x, c) : h < 300 ? (x, 0d, c) : (c, 0d, x);
        return WpfColor.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    internal static (double H, double S, double V) ToHsv(WpfColor color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var d = max - min;
        var h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }

    // Rounded gradient track with a draggable round thumb.
    sealed class GradientSlider : Grid
    {
        public readonly Border Track = new() { Height = 12, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
        readonly Ellipse _thumb = new() { Width = 18, Height = 18, Fill = Brushes.White, Stroke = new SolidColorBrush(WpfColor.FromRgb(0x1B, 0x1F, 0x24)), StrokeThickness = 2, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, IsHitTestVisible = false };
        readonly double _max; readonly Action<double> _changed; double _value;
        public GradientSlider(double max, Action<double> changed)
        {
            _max = max; _changed = changed; Height = 20; Background = Brushes.Transparent; Cursor = Cursors.Hand; Focusable = true;
            Children.Add(Track); Children.Add(_thumb);
            MouseLeftButtonDown += (_, e) => { CaptureMouse(); Focus(); Pick(e.GetPosition(this).X); };
            MouseMove += (_, e) => { if (IsMouseCaptured) Pick(e.GetPosition(this).X); };
            MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
            KeyDown += (_, e) => { var step = e.Key is Key.Left or Key.Down ? -1 : e.Key is Key.Right or Key.Up ? 1 : 0; if (step != 0) { _changed(Math.Clamp(_value + step, 0, _max)); e.Handled = true; } };
            SizeChanged += (_, _) => Place();
        }
        public double Value { get => _value; set { _value = value; Place(); } }
        void Pick(double x) => _changed(Math.Clamp((x - 9) / Math.Max(1, ActualWidth - 18), 0, 1) * _max);
        void Place() => _thumb.Margin = new Thickness(_value / _max * Math.Max(0, ActualWidth - 18), 0, 0, 0);
    }
}
