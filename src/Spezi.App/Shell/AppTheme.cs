using System.Globalization;
using System.Windows.Media;
using Spezi.Core;
using Color = System.Windows.Media.Color;

namespace Spezi.App;

static class AppTheme
{
    static string? _applied;
    public static Color Parse(string? hex)
    {
        var value = AppSettings.ValidAccentColor(hex);
        return Color.FromRgb(byte.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber), byte.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber), byte.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber));
    }

    static Color Mix(Color from, Color to, double amount) => Color.FromRgb((byte)Math.Round(from.R + (to.R - from.R) * amount), (byte)Math.Round(from.G + (to.G - from.G) * amount), (byte)Math.Round(from.B + (to.B - from.B) * amount));

    static double Luminance(Color color)
    {
        static double Channel(byte value) { var c = value / 255d; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush Soft(string? hex) => Brush(Mix(Color.FromRgb(30, 33, 38), Parse(hex), 0.22));
    public static SolidColorBrush Text(string? hex)
    {
        var color = Parse(hex);
        return Brush(Luminance(color) < 0.18 ? Mix(color, Color.FromRgb(244, 247, 248), 0.5) : color);
    }

    public static void Apply(string? hex)
    {
        var value = AppSettings.ValidAccentColor(hex);
        if (_applied == value) return;
        var resources = System.Windows.Application.Current.Resources;
        var color = Parse(value);
        resources["Accent"] = Brush(color);
        resources["AccentHover"] = Brush(Mix(color, Color.FromRgb(255, 255, 255), 0.18));
        resources["AccentInk"] = Brush(Luminance(color) > 0.179 ? Color.FromRgb(17, 24, 32) : Color.FromRgb(255, 255, 255));
        resources["AccentSoft"] = Soft(value);
        resources["AccentText"] = Text(value);
        _applied = value;
    }
}
