using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// A driver table's name cell. Writes the name in the table's chosen <see cref="DriverNameFormat"/>
/// and, for <see cref="DriverNameFormat.Auto"/>, picks the longest form that fits the room the cell
/// actually has — which changes with the widget's size, the theme's font and the PIT and penalty
/// badges that take their room out of the name. Wraps the TextBlock that draws the name (and that the
/// row triggers recolour and embolden) and sets its text before measuring it, reading its font so
/// the fit is judged in the type actually on screen.
/// </summary>
public sealed class DriverNameText : Decorator
{
    public static readonly DependencyProperty FullNameProperty = DependencyProperty.Register(
        nameof(FullName), typeof(string), typeof(DriverNameText),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(DriverNameFormat), typeof(DriverNameText),
        new FrameworkPropertyMetadata(DriverNameFormat.Auto, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty UppercaseSurnameProperty = DependencyProperty.Register(
        nameof(UppercaseSurname), typeof(bool), typeof(DriverNameText),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    // Widths of texts already measured, per font. Rows are rebuilt every update, so the same names
    // come back again and again; this keeps a refresh from shaping each candidate anew.
    private static readonly Dictionary<(string Text, Typeface Typeface, double Size), double> Widths = [];
    private const int MaxCachedWidths = 4096;

    /// <summary>The name as iRacing registered it.</summary>
    public string FullName
    {
        get => (string)GetValue(FullNameProperty);
        set => SetValue(FullNameProperty, value);
    }

    public DriverNameFormat Format
    {
        get => (DriverNameFormat)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public bool UppercaseSurname
    {
        get => (bool)GetValue(UppercaseSurnameProperty);
        set => SetValue(UppercaseSurnameProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not TextBlock text)
        {
            return base.MeasureOverride(constraint);
        }

        var candidates = DriverNameFormatter.Candidates(FullName, Format, UppercaseSurname);
        var chosen = candidates[^1];
        if (candidates.Count > 1)
        {
            var room = constraint.Width - text.Margin.Left - text.Margin.Right - text.Padding.Left - text.Padding.Right;
            chosen = double.IsInfinity(room)
                ? candidates[0]
                : candidates.FirstOrDefault(candidate => WidthOf(candidate, text) <= room + 0.5) ?? chosen;
        }

        // The child's measure just below picks the new text up.
        if (!string.Equals(text.Text, chosen, StringComparison.Ordinal))
        {
            text.Text = chosen;
        }

        return base.MeasureOverride(constraint);
    }

    private static double WidthOf(string candidate, TextBlock text)
    {
        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        var key = (candidate, typeface, text.FontSize);
        if (Widths.TryGetValue(key, out var width))
        {
            return width;
        }

        if (Widths.Count >= MaxCachedWidths)
        {
            Widths.Clear();
        }

        var formatted = new FormattedText(
            candidate,
            CultureInfo.CurrentUICulture,
            text.FlowDirection,
            typeface,
            text.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(text).PixelsPerDip);
        width = formatted.WidthIncludingTrailingWhitespace;
        Widths[key] = width;
        return width;
    }
}
