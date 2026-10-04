using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Layouts;

/// <summary>A rectangle in layout pixels.</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;
}

/// <summary>An alignment guide to draw: a vertical line at x = <see cref="Position"/>, or a
/// horizontal one at y = <see cref="Position"/>.</summary>
public readonly record struct Guide(bool IsVertical, double Position);

public readonly record struct SnapResult(double X, double Y, IReadOnlyList<Guide> Guides);

/// <summary>The geometry behind the editor's canvas: snapping, alignment guides, size levels and
/// whether a widget fits on screen. Kept apart from the window so it can be tested without one.</summary>
public static class LayoutGeometry
{
    /// <summary>
    /// Where a widget being dragged should land. On each axis, an edge or the centre of the widget
    /// that comes within <paramref name="threshold"/> of an edge or centre of another widget or of
    /// the canvas lines up with it exactly and produces a guide; alignment wins over the grid, since
    /// lining things up is the more deliberate intent. An axis that aligns with nothing snaps its
    /// leading edge to the grid when one is given.
    /// </summary>
    public static SnapResult Snap(Box moving, IEnumerable<Box> others, double canvasWidth, double canvasHeight, double threshold, int? grid)
    {
        var targets = others.Append(new Box(0, 0, canvasWidth, canvasHeight)).ToList();
        var guides = new List<Guide>();

        var x = SnapAxis(
            moving.X,
            [0, moving.Width / 2, moving.Width],
            targets.SelectMany(box => new[] { box.X, box.CenterX, box.Right }),
            threshold,
            grid,
            out var vertical);
        if (vertical is { } verticalLine)
        {
            guides.Add(new Guide(IsVertical: true, verticalLine));
        }

        var y = SnapAxis(
            moving.Y,
            [0, moving.Height / 2, moving.Height],
            targets.SelectMany(box => new[] { box.Y, box.CenterY, box.Bottom }),
            threshold,
            grid,
            out var horizontal);
        if (horizontal is { } horizontalLine)
        {
            guides.Add(new Guide(IsVertical: false, horizontalLine));
        }

        return new SnapResult(x, y, guides);
    }

    /// <summary>The size level whose factor is closest to <paramref name="factor"/>. The ends of the
    /// ladder are the minimum and maximum size every widget has.</summary>
    public static ScaleLevel NearestLevel(double factor) =>
        Enum.GetValues<ScaleLevel>().MinBy(level => Math.Abs(ScaleLevels.FactorOf(level) - factor));

    /// <summary>True when any part of the widget lies outside the canvas.</summary>
    public static bool IsOffCanvas(Box widget, double canvasWidth, double canvasHeight) =>
        widget.X < 0 || widget.Y < 0 || widget.Right > canvasWidth || widget.Bottom > canvasHeight;

    private static double SnapAxis(
        double start,
        double[] offsets,
        IEnumerable<double> lines,
        double threshold,
        int? grid,
        out double? guide)
    {
        guide = null;
        var best = threshold;
        double? snapped = null;
        foreach (var line in lines)
        {
            foreach (var offset in offsets)
            {
                var distance = Math.Abs(start + offset - line);
                if (distance <= best)
                {
                    best = distance;
                    snapped = line - offset;
                    guide = line;
                }
            }
        }

        if (snapped is { } aligned)
        {
            return aligned;
        }

        return grid is > 0 ? Math.Round(start / grid.Value) * grid.Value : start;
    }
}
