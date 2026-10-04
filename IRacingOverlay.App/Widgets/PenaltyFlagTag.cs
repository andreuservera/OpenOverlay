using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// The penalty flag on a driver-table row: one flag cloth, or two stacked like cards, the one in
/// front whole and the other peeking out behind it, up and to the right. Black is plain black, the meatball has its orange disc, the
/// furled black its white cross. Colours come from the flag catalog, so a tag always matches the
/// Flags widget's own icon.
/// </summary>
public sealed class PenaltyFlagTag : FrameworkElement
{
    private static readonly Brush Cloth = Frozen(FlagCatalog.Get(FlagKind.Black).Background);
    private static readonly Brush Disc = Frozen(FlagCatalog.Get(FlagKind.Meatball).Foreground);
    private static readonly Brush CrossInk = Frozen(FlagCatalog.Get(FlagKind.Furled).Foreground);
    // A soft hairline keeps a black cloth readable on a dark row without a bright frame round it.
    private static readonly Pen ClothEdge = FrozenPen(Frozen("#73FFFFFF"), 1);
    private const double ClothRadius = 2;
    // How far the back flag of a pair peeks out past the front one.
    private const double PeekX = 6;
    private const double PeekY = 4;
    // Separates the front flag from the one behind it, so their two black cloths don't merge.
    private static readonly Pen Cut = FrozenPen(Frozen("#F20B0D10"), 2.5);

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

        var cloth = new Rect(0, 0, w, h);
        if (Secondary == PenaltyFlag.None)
        {
            DrawCloth(dc, Primary, cloth);
            return;
        }

        var (front, back) = Stacking(Primary, Secondary);
        var size = new Size(Math.Max(0, w - PeekX), Math.Max(0, h - PeekY));
        var frontCloth = new Rect(new Point(0, PeekY), size);
        DrawCloth(dc, back, new Rect(new Point(PeekX, 0), size));
        dc.DrawRoundedRectangle(null, Cut, frontCloth, ClothRadius, ClothRadius);
        DrawCloth(dc, front, frontCloth);
    }

    /// <summary>Which of a pair goes in front. The more serious one (the first) does, unless it is
    /// the plain black flag: that has no mark to lose, so a meatball's disc or a furled cross takes
    /// the front instead of being hidden behind it.</summary>
    internal static (PenaltyFlag Front, PenaltyFlag Back) Stacking(PenaltyFlag primary, PenaltyFlag secondary) =>
        primary == PenaltyFlag.Black && secondary != PenaltyFlag.Black ? (secondary, primary) : (primary, secondary);

    /// <summary>One flag cloth: black with its mark, rounded a touch, edged by a hairline.</summary>
    private static void DrawCloth(DrawingContext dc, PenaltyFlag flag, Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(area, ClothRadius, ClothRadius));
        dc.DrawRectangle(Cloth, null, area);
        DrawFlag(dc, flag, area);
        dc.Pop();
        dc.DrawRoundedRectangle(null, ClothEdge, new Rect(area.X + 0.5, area.Y + 0.5, area.Width - 1, area.Height - 1), ClothRadius, ClothRadius);
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
