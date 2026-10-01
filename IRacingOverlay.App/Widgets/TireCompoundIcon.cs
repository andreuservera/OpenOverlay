using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// A tyre compound as a ringed letter. The letter is drawn as geometry and centred on its own ink,
/// not on the text line: a text box centres the font's line box (ascent, descent, side bearings), which
/// leaves a capital sitting off-centre by a different amount for every letter and size.
/// </summary>
public sealed class TireCompoundIcon : FrameworkElement
{
    private static readonly ConcurrentDictionary<string, Brush> BrushCache = new();
    private static readonly ConcurrentDictionary<(string Text, string Family, double EmSize, double PixelsPerDip), Geometry> GlyphCache = new();
    private static readonly Brush Sidewall = Frozen(Color.FromRgb(0x0B, 0x0D, 0x10));

    public static readonly DependencyProperty LetterProperty = DependencyProperty.Register(
        nameof(Letter), typeof(string), typeof(TireCompoundIcon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(
        nameof(Ink), typeof(string), typeof(TireCompoundIcon),
        new FrameworkPropertyMetadata("#C4CCD4", FrameworkPropertyMetadataOptions.AffectsRender));

    public string Letter
    {
        get => (string)GetValue(LetterProperty);
        set => SetValue(LetterProperty, value);
    }

    /// <summary>Hex colour of the ring and the letter.</summary>
    public string Ink
    {
        get => (string)GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var ring = size * 0.15;
        var ink = BrushOf(Ink);
        dc.DrawEllipse(Sidewall, new Pen(ink, ring), center, (size - ring) / 2, (size - ring) / 2);

        if (string.IsNullOrEmpty(Letter) || GlyphOf(Letter, size * 0.56, VisualTreeHelper.GetDpi(this).PixelsPerDip) is not { } glyph)
        {
            return;
        }

        dc.PushTransform(new MatrixTransform(Placement(glyph.Bounds, center, innerDiameter: size - (2 * ring))));
        dc.DrawGeometry(ink, null, glyph);
        dc.Pop();
    }

    /// <summary>Moves the glyph's ink box onto <paramref name="center"/>, shrinking a wide one ("10")
    /// to fit inside the ring.</summary>
    internal static Matrix Placement(Rect ink, Point center, double innerDiameter)
    {
        var scale = Math.Min(1, innerDiameter * 0.72 / Math.Max(ink.Width, ink.Height));
        var matrix = Matrix.Identity;
        matrix.Translate(-(ink.X + (ink.Width / 2)), -(ink.Y + (ink.Height / 2)));
        matrix.Scale(scale, scale);
        matrix.Translate(center.X, center.Y);
        return matrix;
    }

    private Geometry? GlyphOf(string text, double emSize, double pixelsPerDip)
    {
        // The dashboard's themes swap this family, so it's part of the key.
        var family = TryFindResource("Theme.NumericFontFamily") as FontFamily ?? new FontFamily("Bahnschrift");
        var glyph = GlyphCache.GetOrAdd((text, family.Source, Math.Round(emSize, 2), pixelsPerDip), key =>
        {
            var formatted = new FormattedText(
                key.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                key.EmSize, Brushes.Black, key.PixelsPerDip);
            var geometry = formatted.BuildGeometry(new Point(0, 0));
            geometry.Freeze();
            return geometry;
        });
        return glyph.Bounds.IsEmpty ? null : glyph;
    }

    private static Brush BrushOf(string hex) => BrushCache.GetOrAdd(hex, static value =>
        Frozen(ColorConverter.ConvertFromString(value) is Color color ? color : Colors.Gray));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
