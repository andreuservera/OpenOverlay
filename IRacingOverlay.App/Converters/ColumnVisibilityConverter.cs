using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IRacingOverlay.App.Converters;

/// <summary>
/// Turns a "should this column show" bool into whatever the binding target actually needs:
/// a Visibility for a header/cell TextBlock, or a GridLength for the ColumnDefinition itself —
/// hiding only the content and leaving the column's width in place would leave a blank gap.
/// ConverterParameter is the column's normal width (as a string), used when visible.
/// </summary>
public sealed class ColumnVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is true;

        if (targetType == typeof(GridLength))
        {
            var width = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 50;
            return visible ? new GridLength(width) : new GridLength(0);
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
