using System.Windows;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>Where one module goes. <see cref="FullHeight"/> is true when it has its column to itself.</summary>
internal sealed record CockpitCell(CockpitModule Module, Rect Bounds, bool FullHeight);

/// <summary>
/// Where everything on the cockpit goes, at scale M: one panel holding the module cards, the
/// shift-light strip over them and the proximity bars down each side. A pure function of what is
/// shown, so it is tested apart from the drawing.
///
/// The rule that keeps it free of gaps: the dashboard is a row of columns, all two rows high. A
/// large module fills a column. Two small modules in a row share one, stacked. A small module on
/// its own — followed by a large one, or last — fills its column, at full height. Columns take their
/// modules' preferred widths, and if together they are narrower than the shift lights need, every
/// column widens in proportion.
/// </summary>
internal sealed record CockpitLayout(
    Size Size,
    IReadOnlyList<CockpitCell> Cells,
    Rect? ShiftLights,
    Rect? LeftRadar,
    Rect? RightRadar)
{
    public const double RowHeight = 34;
    public const double Gap = 4;
    public const double BodyHeight = (2 * RowHeight) + Gap;
    public const double ShiftLightsHeight = 10;
    public const double RadarWidth = 6;

    /// <summary>Space between the panel's edge and what it holds.</summary>
    public const double Padding = 6;

    /// <summary>The narrowest the module area gets: enough for 14 legible shift lights.</summary>
    public const double MinBodyWidth = 150;

    public static CockpitLayout Empty { get; } = new(new Size(0, 0), [], null, null, null);

    public static CockpitLayout Compose(IReadOnlyList<CockpitModule> modules, bool shiftLights, bool radar)
    {
        if (modules.Count == 0 && !shiftLights && !radar)
        {
            return Empty;
        }

        var columns = Columns(modules);
        var bodyWidth = Fit(columns);
        var bodyX = Padding + (radar ? RadarWidth + Gap : 0);
        var bodyY = Padding + (shiftLights ? ShiftLightsHeight + Gap : 0);

        var cells = new List<CockpitCell>();
        var x = bodyX;
        foreach (var column in columns)
        {
            if (column.Modules.Count == 1)
            {
                cells.Add(new CockpitCell(column.Modules[0], new Rect(x, bodyY, column.Width, BodyHeight), FullHeight: true));
            }
            else
            {
                cells.Add(new CockpitCell(column.Modules[0], new Rect(x, bodyY, column.Width, RowHeight), FullHeight: false));
                cells.Add(new CockpitCell(column.Modules[1], new Rect(x, bodyY + RowHeight + Gap, column.Width, RowHeight), FullHeight: false));
            }

            x += column.Width + Gap;
        }

        // With no modules the lights stand alone; the radar keeps a module row's height either way,
        // so its bars stay readable.
        var inner = modules.Count > 0 || radar ? bodyY - Padding + BodyHeight : ShiftLightsHeight;
        var width = bodyX + bodyWidth + (radar ? Gap + RadarWidth : 0) + Padding;
        return new CockpitLayout(
            new Size(width, inner + (2 * Padding)),
            cells,
            shiftLights ? new Rect(bodyX, Padding, bodyWidth, ShiftLightsHeight) : null,
            radar ? new Rect(Padding, Padding, RadarWidth, inner) : null,
            radar ? new Rect(width - Padding - RadarWidth, Padding, RadarWidth, inner) : null);
    }

    private sealed class Column(List<CockpitModule> modules, double width)
    {
        public List<CockpitModule> Modules { get; } = modules;

        public double Width { get; set; } = width;
    }

    private static List<Column> Columns(IReadOnlyList<CockpitModule> modules)
    {
        var columns = new List<Column>();
        for (var i = 0; i < modules.Count; i++)
        {
            var spec = CockpitModules.Of(modules[i]);
            if (!spec.IsLarge && i + 1 < modules.Count && CockpitModules.Of(modules[i + 1]) is { IsLarge: false } next)
            {
                columns.Add(new Column([modules[i], modules[i + 1]], Math.Max(spec.Width, next.Width)));
                i++;
            }
            else
            {
                columns.Add(new Column([modules[i]], spec.Width));
            }
        }

        return columns;
    }

    /// <summary>Widens the columns in proportion until they span <see cref="MinBodyWidth"/>, and
    /// returns the width they span.</summary>
    private static double Fit(List<Column> columns)
    {
        if (columns.Count == 0)
        {
            return MinBodyWidth;
        }

        var gaps = (columns.Count - 1) * Gap;
        var widths = columns.Sum(column => column.Width);
        if (widths + gaps < MinBodyWidth)
        {
            var factor = (MinBodyWidth - gaps) / widths;
            foreach (var column in columns)
            {
                column.Width *= factor;
            }

            return MinBodyWidth;
        }

        return widths + gaps;
    }
}
