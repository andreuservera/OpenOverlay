using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace IRacingOverlay.App.Converters;

/// <summary>
/// A "#RRGGBB" color string → a solid brush of that color at <c>ConverterParameter</c> opacity
/// (0–1). Lets a chip be tinted by its meaning (licence class) without the saturated fill that used
/// to make every chip the loudest thing on its row. Frozen and cached per color/opacity pair.
/// </summary>
public sealed class ColorAlphaConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<(Color, double), Brush> Cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!ColorParsing.TryParse(value, out var color))
        {
            return Brushes.Transparent;
        }

        var alpha = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            ? Math.Clamp(a, 0, 1)
            : 1.0;

        return Cache.GetOrAdd((color, alpha), static key =>
        {
            var (c, opacity) = key;
            var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        });
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal static class ColorParsing
{
    public static bool TryParse(object value, out Color color)
    {
        switch (value)
        {
            case Color c:
                color = c;
                return true;
            case string s when !string.IsNullOrWhiteSpace(s):
                try
                {
                    if (ColorConverter.ConvertFromString(s) is Color parsed)
                    {
                        color = parsed;
                        return true;
                    }
                }
                catch (FormatException)
                {
                }

                break;
        }

        color = default;
        return false;
    }
}
