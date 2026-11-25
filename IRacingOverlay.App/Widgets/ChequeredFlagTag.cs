using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>
/// The chequered flag on a driver-table row whose car has finished: a cloth of four rows of
/// squares, in the same footprint, rounding and hairline as the penalty flag tag, so the row's
/// tags read as one family. Colours come from the flag catalog, like the Flags widget's own icon.
/// </summary>
public sealed class ChequeredFlagTag : FrameworkElement
{
    private static readonly Brush Light = Frozen(FlagCatalog.Get(FlagKind.Checkered).Background);
    private static readonly Brush Dark = Frozen(FlagCatalog.Get(FlagKind.Checkered).Foreground);
    private static readonly Pen ClothEdge = FrozenPen(Frozen("#73FFFFFF"), 1);
    private const double ClothRadius = 2;
    private const int Rows = 4;

    public ChequeredFlagTag()
    {
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var area = new Rect(0, 0, ActualWidth, ActualHeight);
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        // Square squares: as many columns as fit at the rows' height.
        var columns = Math.Max(1, (int)Math.Round(area.Width / (area.Height / Rows)));
        var cellWidth = area.Width / columns;
        var cellHeight = area.Height / Rows;

        dc.PushClip(new RectangleGeometry(area, ClothRadius, ClothRadius));
        dc.DrawRectangle(Light, null, area);
        for (var row = 0; row < Rows; row++)
        {
            for (var column = (row % 2); column < columns; column += 2)
            {
                dc.DrawRectangle(Dark, null, new Rect(column * cellWidth, row * cellHeight, cellWidth, cellHeight));
            }
        }

        dc.Pop();
        dc.DrawRoundedRectangle(null, ClothEdge, new Rect(0.5, 0.5, area.Width - 1, area.Height - 1), ClothRadius, ClothRadius);
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
