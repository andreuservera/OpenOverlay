using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace IRacingOverlay.App.Converters;

/// <summary>
/// A color string ("#RRGGBB", "#AARRGGBB" or a named color) → the same frozen brush every time for
/// the same string. Binding a Brush property straight to a string makes WPF's default converter build
/// a new brush on every evaluation; a new instance counts as a changed value, so each table row
/// re-rendered its text every tick even when its colours hadn't moved.
/// </summary>
public sealed class CachedBrushConverter : IValueConverter
{
    private static readonly BrushConverter Parser = new();
    private static readonly ConcurrentDictionary<string, Brush> Cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text || text.Length == 0)
        {
            return value as Brush;
        }

        return Cache.GetOrAdd(text, static key =>
        {
            var brush = (Brush)Parser.ConvertFromInvariantString(key)!;
            brush.Freeze();
            return brush;
        });
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
