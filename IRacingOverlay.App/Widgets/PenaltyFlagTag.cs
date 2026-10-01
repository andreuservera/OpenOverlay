using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// The penalty flag on a driver-table row: one flag, or two side by side in the same footprint, split
/// down the middle. Black is plain black, the meatball has its orange disc, the furled black its white
/// cross. Colours come from the flag catalog, so a tag always matches the Flags widget's own icon.
/// </summary>
public sealed class PenaltyFlagTag : FrameworkElement
{
    private static readonly Brush Cloth = Frozen(FlagCatalog.Get(FlagKind.Black).Background);
    private static readonly Brush Disc = Frozen(FlagCatalog.Get(FlagKind.Meatball).Foreground);
    private static readonly Brush CrossInk = Frozen(FlagCatalog.Get(FlagKind.Furled).Foreground);
    // The edge keeps a black flag readable on a dark row.
    private static readonly Pen Edge = FrozenPen(Frozen("#B3F2F5F8"), 1);
    private const double Radius = 2;

    public static readonly DependencyProperty PrimaryProperty = DependencyProperty.Register(
        nameof(Primary), typeof(PenaltyFlag), typeof(PenaltyFlagTag),
        new FrameworkPropertyMetadata(PenaltyFlag.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(
        nameof(Secondary), typeof(PenaltyFlag), typeof(PenaltyFlagTag),
        new FrameworkPropertyMetadata(PenaltyFlag.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public PenaltyFlag Primary
    {
        get => (PenaltyFlag)GetValue(PrimaryProperty);
        set => SetValue(PrimaryProperty, value);
    }

    public PenaltyFlag Secondary
    {
        get => (PenaltyFlag)GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    public PenaltyFlagTag()
    {
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (Primary == PenaltyFlag.None || w <= 0 || h <= 0)
        {
            return;
        }

        var bounds = new Rect(0, 0, w, h);
        dc.PushClip(new RectangleGeometry(bounds, Radius, Radius));
        dc.DrawRectangle(Cloth, null, bounds);
        if (Secondary == PenaltyFlag.None)
        {
            DrawFlag(dc, Primary, bounds);
        }
        else
        {
            DrawFlag(dc, Primary, new Rect(0, 0, w / 2, h));
            DrawFlag(dc, Secondary, new Rect(w / 2, 0, w / 2, h));
            dc.DrawLine(Edge, new Point(Math.Round(w / 2) + 0.5, 1), new Point(Math.Round(w / 2) + 0.5, h - 1));
        }

        dc.Pop();
        dc.DrawRoundedRectangle(null, Edge, new Rect(0.5, 0.5, w - 1, h - 1), Radius, Radius);
    }

    private static void DrawFlag(DrawingContext dc, PenaltyFlag flag, Rect area)
    {
        switch (flag)
        {
            case PenaltyFlag.Meatball:
                var diameter = Math.Min(area.Height * 0.57, area.Width * 0.64);
                dc.DrawEllipse(Disc, null, new Point(area.X + (area.Width / 2), area.Y + (area.Height / 2)), diameter / 2, diameter / 2);
                break;
            case PenaltyFlag.Furled:
                // Corner to corner of its own half, clipped so the arms end square at the edges.
                var pen = new Pen(CrossInk, Math.Max(1.2, area.Height * 0.11));
                dc.PushClip(new RectangleGeometry(area));
                dc.DrawLine(pen, area.TopLeft, area.BottomRight);
                dc.DrawLine(pen, area.TopRight, area.BottomLeft);
                dc.Pop();
                break;
        }
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
