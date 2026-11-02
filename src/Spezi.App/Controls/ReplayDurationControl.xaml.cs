using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Spezi.Core;

namespace Spezi.App;

public partial class ReplayDurationControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SecondsProperty = DependencyProperty.Register(nameof(Seconds), typeof(int), typeof(ReplayDurationControl),
        new FrameworkPropertyMetadata(180, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, SecondsChanged, (_, value) => AppSettings.ValidReplaySeconds((int)value)));
    public int Seconds { get => (int)GetValue(SecondsProperty); set => SetValue(SecondsProperty, value); }
    public event RoutedPropertyChangedEventHandler<int>? DurationChanged;
    bool _syncing;

    public ReplayDurationControl()
    {
        InitializeComponent();
        UpdateValue();
        Loaded += (_, _) => DrawMarks();
    }

    static void SecondsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (ReplayDurationControl)sender;
        control.UpdateValue();
        control.DurationChanged?.Invoke(control, new RoutedPropertyChangedEventArgs<int>((int)e.OldValue, (int)e.NewValue));
    }

    void UpdateValue()
    {
        if (DurationSlider == null || DurationText == null) return;
        _syncing = true;
        DurationSlider.Value = Seconds;
        DurationText.Text = AppSettings.ReplayDurationText(Seconds);
        DurationSlider.ToolTip = DurationText.Text;
        _syncing = false;
    }

    void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || DurationSlider == null) return;
        SetCurrentValue(SecondsProperty, AppSettings.ValidReplaySeconds((int)Math.Round(e.NewValue)));
        UpdateValue();
    }

    void MarksSizeChanged(object sender, SizeChangedEventArgs e) => DrawMarks();

    void DrawMarks()
    {
        if (Marks == null || Marks.ActualWidth <= 20) return;
        Marks.Children.Clear();
        var width = Marks.ActualWidth;
        for (var seconds = 15; seconds <= 300; seconds += 15)
        {
            var x = 10 + (width - 20) * (seconds - 15) / 285;
            var minute = seconds % 60 == 0;
            var diameter = minute ? 6d : 2.5d;
            var dot = new Ellipse { Width = diameter, Height = diameter, Tag = seconds, Opacity = minute ? 1 : 0.45 };
            dot.SetResourceReference(Shape.FillProperty, "Muted");
            Canvas.SetLeft(dot, x - diameter / 2);
            Canvas.SetTop(dot, 4 - diameter / 2);
            Marks.Children.Add(dot);
            if (!minute && seconds != 15) continue;
            var label = new TextBlock { Text = minute ? $"{seconds / 60}m" : "15s", FontSize = 10, Tag = seconds };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Clamp(x - label.DesiredSize.Width / 2, 0, Math.Max(0, width - label.DesiredSize.Width)));
            Canvas.SetTop(label, 12);
            Marks.Children.Add(label);
        }
    }
}
