using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// A tyre compound, drawn like the tyre seen side-on: dark rubber with a lit outer edge, the
/// compound's colour as a sidewall stripe broken at either side (the way the band reads on a real
/// sidewall), and the compound letter in the middle. The letter is drawn as geometry and centred on its own ink,
/// not on the text line: a text box centres the font's line box (ascent, descent, side bearings), which
/// leaves a capital sitting off-centre by a different amount for every letter and size.
/// </summary>
public sealed class TireCompoundIcon : FrameworkElement
{
    private static readonly ConcurrentDictionary<string, Brush> BrushCache = new();
    private static readonly ConcurrentDictionary<(string Text, string Family, double EmSize, double PixelsPerDip), Geometry> GlyphCache = new();
    private static readonly Brush Rubber = Frozen(Color.FromRgb(0x14, 0x17, 0x1B));
    private static readonly Brush Edge = Frozen(Color.FromRgb(0x3A, 0x41, 0x4A));

    /// <summary>How much of each half the stripe covers, in degrees; the rest is the side gaps.</summary>
    private const double StripeSweep = 132;

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
        var ink = BrushOf(Ink);
        var outer = (size / 2) - 0.5;
        dc.DrawEllipse(Rubber, new Pen(Edge, 1), center, outer, outer);

        // The stripe: two arcs, over the top and under the bottom, with round ends.
        var stripe = Math.Max(1.6, size * 0.13);
        var stripeRadius = outer * 0.74;
        var pen = new Pen(ink, stripe) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, Arc(center, stripeRadius, -StripeSweep / 2, StripeSweep / 2));
        dc.DrawGeometry(null, pen, Arc(center, stripeRadius, 180 - (StripeSweep / 2), 180 + (StripeSweep / 2)));

        if (string.IsNullOrEmpty(Letter) || GlyphOf(Letter, size * 0.56, VisualTreeHelper.GetDpi(this).PixelsPerDip) is not { } glyph)
        {
            return;
        }

        dc.PushTransform(new MatrixTransform(Placement(glyph.Bounds, center, innerDiameter: (2 * stripeRadius) - stripe)));
        dc.DrawGeometry(ink, null, glyph);
        dc.Pop();
    }

    /// <summary>An arc on a circle, angles in degrees clockwise from straight up.</summary>
    private static Geometry Arc(Point center, double radius, double from, double to)
    {
        static Point On(Point c, double r, double degrees)
        {
            var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
            return new Point(c.X + (sin * r), c.Y - (cos * r));
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(On(center, radius, from), false, false);
            g.ArcTo(On(center, radius, to), new Size(radius, radius), 0, to - from > 180, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
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
