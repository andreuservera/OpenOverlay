using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>Collapses a row whose optional text (a hint, a unit suffix) simply isn't there, so the
/// same template serves settings that need an explanation and settings that don't.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Bool to Visibility, with <see cref="Invert"/> so a single converter type covers both
/// directions instead of needing a second class that only differs by a negation.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
