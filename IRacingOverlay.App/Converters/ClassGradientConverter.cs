using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace IRacingOverlay.App.Converters;

/// <summary>
/// Class color → the wash behind a multiclass row: the color at <c>ConverterParameter</c> opacity
/// against the class bar, fading to nothing well before the numeric columns so the lap times sit on
/// the plain panel. Brushes are frozen and cached per color, since rows are re-templated every tick.
/// </summary>
public sealed class ClassGradientConverter : IValueConverter
{
    private const double DefaultStrength = 0.3;

    private static readonly ConcurrentDictionary<(Color, double), Brush> Cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!ColorParsing.TryParse(value, out var color))
        {
            return Brushes.Transparent;
        }

        var strength = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)
            ? p
            : DefaultStrength;

        return Cache.GetOrAdd((color, strength), static key => Build(key.Item1, key.Item2));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    // Eased rather than linear: a linear ramp shows a visible "end" where it hits zero, this one
    // just dissolves into the panel.
    private static Brush Build(Color color, double strength)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        brush.GradientStops.Add(Stop(color, strength, 0));
        brush.GradientStops.Add(Stop(color, strength * 0.45, 0.16));
        brush.GradientStops.Add(Stop(color, strength * 0.14, 0.38));
        brush.GradientStops.Add(Stop(color, 0, 0.62));
        brush.Freeze();
        return brush;
    }

    private static GradientStop Stop(Color color, double alpha, double offset) =>
        new(Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B), offset);
}
