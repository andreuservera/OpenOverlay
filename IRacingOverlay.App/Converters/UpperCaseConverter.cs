using System.Globalization;
using System.Windows.Data;

namespace IRacingOverlay.App.Converters;

/// <summary>A widget title in capitals, in the current language's casing rules. With the
/// parameter "HasText" it answers whether there is any title at all instead.</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        return parameter as string == "HasText"
            ? !string.IsNullOrWhiteSpace(text)
            : text?.ToUpper(CultureInfo.CurrentUICulture) ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
