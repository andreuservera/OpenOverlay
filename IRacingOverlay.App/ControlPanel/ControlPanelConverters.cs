using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

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

/// <summary>A text field whose value is applied on Enter as well as when it loses focus: typing a
/// number and pressing Enter is how anyone expects a field like "73 %" to take effect.</summary>
public static class CommitOnEnter
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(CommitOnEnter), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox box && e.NewValue is true)
        {
            box.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    box.SelectAll();
                    args.Handled = true;
                }
            };
            // Clicking in selects the value, so a new one replaces it rather than joining it.
            box.GotKeyboardFocus += (_, _) => box.SelectAll();
        }
    }
}
