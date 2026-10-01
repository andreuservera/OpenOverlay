using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// The flag graphic, drawn straight into the visual with a DrawingContext: no child elements, no
/// image assets, crisp at every scale level. Shared by the widget, the dashboard and the preview.
/// </summary>
public sealed class FlagIcon : FrameworkElement
{
    private static readonly ConcurrentDictionary<string, Brush> BrushCache = new();
    private static readonly Pen Edge = FrozenPen(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 1);
    private static readonly Brush DimLamp = Frozen(Color.FromRgb(0x3A, 0x1A, 0x1A));
    private static readonly Brush[] MixedBands =
    [
        BrushOf("#1FAA59"), BrushOf("#F7C600"), BrushOf("#F2F5F8"), BrushOf("#E03131"),
    ];

    public static readonly DependencyProperty FlagProperty = DependencyProperty.Register(
        nameof(Flag), typeof(FlagState), typeof(FlagIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public FlagState? Flag
    {
        get => (FlagState?)GetValue(FlagProperty);
        set => SetValue(FlagProperty, value);
    }

    public FlagIcon()
    {
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var flag = Flag;
        var w = ActualWidth;
        var h = ActualHeight;
        if (flag is null || w <= 0 || h <= 0)
        {
            return;
        }

        var bounds = new Rect(0, 0, w, h);
        var radius = Math.Min(w, h) * 0.12;
        var background = BrushOf(flag.BackgroundColor);
        var foreground = BrushOf(flag.ForegroundColor);

        dc.PushClip(new RectangleGeometry(bounds, radius, radius));
        dc.DrawRectangle(background, null, bounds);

        switch (flag.Style)
        {
            case FlagVisualStyle.Checkered:
                DrawChecks(dc, foreground, w, h);
                break;
            case FlagVisualStyle.Meatball:
                dc.DrawEllipse(foreground, null, new Point(w / 2, h / 2), h * 0.3, h * 0.3);
                break;
            case FlagVisualStyle.DebrisStripes:
                DrawStripes(dc, foreground, w, h);
                break;
            case FlagVisualStyle.BlueWithOrangeStripe:
                dc.PushTransform(new RotateTransform(-28, w / 2, h / 2));
                dc.DrawRectangle(foreground, null, new Rect(-w, (h / 2) - (h * 0.11), w * 3, h * 0.22));
                dc.Pop();
                break;
            case FlagVisualStyle.CornerCross:
                var thickness = Math.Max(1.5, h * 0.13);
                var diagonal = new Pen(foreground, thickness);
                // Run past the corners: the clip then squares each arm off exactly at the flag's edge.
                var reach = new Vector(w, h) * (thickness / Math.Sqrt((w * w) + (h * h)));
                dc.DrawLine(diagonal, new Point(-reach.X, -reach.Y), new Point(w + reach.X, h + reach.Y));
                dc.DrawLine(diagonal, new Point(w + reach.X, -reach.Y), new Point(-reach.X, h + reach.Y));
                break;
            case FlagVisualStyle.BlackWithCross:
                var cross = new Pen(foreground, h * 0.13) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(cross, new Point(w * 0.3, h * 0.22), new Point(w * 0.7, h * 0.78));
                dc.DrawLine(cross, new Point(w * 0.7, h * 0.22), new Point(w * 0.3, h * 0.78));
                break;
            case FlagVisualStyle.CrossedFlags:
                DrawCrossedFlags(dc, foreground, w, h);
                break;
            case FlagVisualStyle.LapBoard:
                DrawGlyph(dc, flag.Glyph, foreground, w, h);
                break;
            case FlagVisualStyle.StartLights:
                var lamp = flag.Variant == FlagVariant.LightsSet ? foreground : DimLamp;
                foreach (var x in new[] { 0.24, 0.5, 0.76 })
                {
                    dc.DrawEllipse(lamp, null, new Point(w * x, h / 2), h * 0.19, h * 0.19);
                }

                break;
            case FlagVisualStyle.Mixed:
                for (var i = 0; i < MixedBands.Length; i++)
                {
                    dc.DrawRectangle(MixedBands[i], null, new Rect(w * i / MixedBands.Length, 0, (w / MixedBands.Length) + 0.5, h));
                }

                break;
            case FlagVisualStyle.Placeholder:
                dc.DrawLine(new Pen(foreground, 2), new Point(w * 0.35, h / 2), new Point(w * 0.65, h / 2));
                break;
        }

        dc.Pop();
        dc.DrawRoundedRectangle(null, Edge, new Rect(0.5, 0.5, w - 1, h - 1), radius, radius);
    }

    private static void DrawChecks(DrawingContext dc, Brush dark, double w, double h)
    {
        const int columns = 6;
        const int rows = 4;
        var cw = w / columns;
        var ch = h / rows;
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                if ((row + col) % 2 == 0)
                {
                    dc.DrawRectangle(dark, null, new Rect(col * cw, row * ch, cw + 0.25, ch + 0.25));
                }
            }
        }
    }

    private static void DrawStripes(DrawingContext dc, Brush stripe, double w, double h)
    {
        const int bands = 6;
        var bw = w / bands;
        for (var i = 1; i < bands; i += 2)
        {
            dc.DrawRectangle(stripe, null, new Rect(i * bw, 0, bw, h));
        }
    }

    private static void DrawCrossedFlags(DrawingContext dc, Brush ink, double w, double h)
    {
        var pole = new Pen(ink, Math.Max(1, h * 0.07)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(pole, new Point(w * 0.3, h * 0.88), new Point(w * 0.62, h * 0.14));
        dc.DrawLine(pole, new Point(w * 0.7, h * 0.88), new Point(w * 0.38, h * 0.14));
        dc.DrawRectangle(ink, null, new Rect(w * 0.62, h * 0.12, w * 0.2, h * 0.24));
        dc.DrawRectangle(ink, null, new Rect(w * 0.18, h * 0.12, w * 0.2, h * 0.24));
    }

    private void DrawGlyph(DrawingContext dc, string glyph, Brush ink, double w, double h)
    {
        if (glyph.Length == 0)
        {
            return;
        }

        var family = TryFindResource("Theme.NumericFontFamily") as FontFamily ?? new FontFamily("Bahnschrift");
        var text = new FormattedText(
            glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            h * 0.66, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point((w - text.Width) / 2, (h - text.Height) / 2));
    }

    private static Brush BrushOf(string hex) => BrushCache.GetOrAdd(hex, static value =>
        Frozen(ColorConverter.ConvertFromString(value) is Color color ? color : Colors.Gray));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
