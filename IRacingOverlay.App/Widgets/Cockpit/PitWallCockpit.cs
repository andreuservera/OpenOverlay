using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>
/// PIT WALL — race-engineer software adapted for the driver.
///
/// Philosophy: be the driver and the engineer at once; every value exposed, labelled and gridded.
/// Target: data-driven drivers, setup tinkerers, league engineers. Silhouette: a long flat
/// instrument table — a header bar over one row of labelled cells — bracketed by two vertical
/// proximity bars, one each side.
/// Typography: Tahoma — the dense UI face of classic telemetry software; small caps labels, bold
/// values. Containers: a strict grid with ruled cells and no rounding. Colour: workstation grey;
/// green/amber/red only on values that carry a state. Hierarchy: flat by design — every cell weighs
/// the same, the reading order is left to right. Alerts: the gear cell turns red at the shift point,
/// ABS reads ACTIVE, fuel and temperatures turn amber when they need attention, and the side bars
/// light amber where a car is alongside.
/// </summary>
public sealed class PitWallCockpit : CockpitDashboard
{
    private static readonly Typeface Bold = Face("Tahoma", FontWeights.Bold);
    private static readonly Typeface Regular = Face("Tahoma", FontWeights.Normal);
    private static readonly string[] LampColors = ["#4ADE80", "#FACC15", "#F87171"];
    private static readonly (string Label, double Width)[] Columns =
    [
        ("GEAR", 48), ("SPEED  km/h", 78), ("RPM", 70), ("SHIFT", 92), ("ABS", 52),
        ("FUEL", 74), ("INPUTS", 56), ("TEMPS  °C", 72),
    ];

    private const double DashHeight = 84;
    private const double BarWidth = 8;
    private const double BarGap = 4;
    private const double TableX = BarWidth + BarGap;
    private static readonly double TableWidth = Columns.Sum(c => c.Width);

    protected override Size DesignSize => new(TableWidth + (2 * TableX), DashHeight);

    protected override void Draw(DrawingContext dc)
    {
        var tableRight = TableX + TableWidth;
        VerticalBand(dc, State.LeftProximity, new Rect(0, 0, BarWidth, DashHeight), B("#E6101214"), B("#FBBF24"), 0);
        VerticalBand(dc, State.RightProximity, new Rect(tableRight + BarGap, 0, BarWidth, DashHeight), B("#E6101214"), B("#FBBF24"), 0);

        dc.DrawRectangle(B("#F2101214"), P("#3A3F45", 1), new Rect(TableX + 0.5, 0.5, TableWidth - 1, DashHeight - 1));
        dc.DrawRectangle(B("#1B1F24"), null, new Rect(TableX + 1, 1, TableWidth - 2, 15));
        Text(dc, "DRIVER TELEMETRY", Bold, 9, B("#9CA3AF"), TableX + 6, 8.5, v: VAlign.Center);
        Text(dc, $"CH 1–{Columns.Length}", Regular, 9, B("#6B7280"), tableRight - 6, 8.5, HAlign.Right, VAlign.Center);

        var x = TableX;
        for (var c = 0; c < Columns.Length; c++)
        {
            var cell = new Rect(x, 16, Columns[c].Width, DashHeight - 16);
            if (c > 0)
            {
                dc.DrawLine(P("#2A2F35", 1), new Point(x + 0.5, 16), new Point(x + 0.5, DashHeight));
            }

            DrawCell(dc, c, cell);
            Text(dc, Columns[c].Label, Regular, 9, B("#8A939C"), cell.X + 6, cell.Y + 5);
            x += Columns[c].Width;
        }
    }

    private void DrawCell(DrawingContext dc, int column, Rect cell)
    {
        var right = cell.Right - 6;
        var middle = cell.Y + 36;
        switch (column)
        {
            case 0:
                var shift = AtShiftPoint && ShiftLampsOn;
                if (shift)
                {
                    dc.DrawRectangle(B("#3F1212"), null, new Rect(cell.X + 1, cell.Y, cell.Width - 1, cell.Height - 1));
                }

                Text(dc, Gear, Bold, 32, B(shift ? "#F87171" : "#FFFFFF"), cell.X + (cell.Width / 2), middle + 2, HAlign.Center, VAlign.Center);
                break;
            case 1:
                Text(dc, Speed, Bold, 24, B("#FFFFFF"), right, middle, HAlign.Right, VAlign.Center);
                break;
            case 2:
                Text(dc, Rpm, Bold, 18, B("#E5E7EB"), right, middle, HAlign.Right, VAlign.Center);
                break;
            case 3:
                // Lights only: two staggered rows so each lamp stays readable in a narrow cell.
                var w = (cell.Width - 12 - 6) / 7;
                for (var i = 0; i < LampCount; i++)
                {
                    var row = i / 7;
                    var col = i % 7;
                    var color = IsLampLit(i) ? LampColors[LampStage(i)] : "#2A2F35";
                    dc.DrawRectangle(B(color), null, new Rect(cell.X + 6 + (col * (w + 1)), cell.Y + 22 + (row * 20), w, 17));
                }

                break;
            case 4:
                var absColor = AbsActive ? (AbsFlashOn ? "#FBBF24" : "#8A6A1E") : "#6B7280";
                Text(dc, AbsActive ? "ACTIVE" : "OFF", Bold, AbsActive ? 11 : 14, B(absColor), right, middle, HAlign.Right, VAlign.Center);
                break;
            case 5:
                var low = State.FuelPct is < 0.1;
                var fuel = Text(dc, "L", Regular, 10, B("#8A939C"), right, cell.Y + 30, HAlign.Right, VAlign.Center);
                Text(dc, Fuel, Bold, 18, B(low ? "#FBBF24" : "#FFFFFF"), fuel.X - 3, cell.Y + 30, HAlign.Right, VAlign.Center);
                var track = new Rect(cell.X + 6, cell.Y + 48, cell.Width - 12, 6);
                dc.DrawRectangle(B("#2A2F35"), null, track);
                if (State.FuelPct is { } pct)
                {
                    dc.DrawRectangle(B(low ? "#FBBF24" : "#4ADE80"), null,
                        new Rect(track.X, track.Y, track.Width * Math.Clamp(pct, 0, 1), track.Height));
                }

                break;
            case 6:
                Pedal(dc, new Rect(cell.X + 12, cell.Y + 20, 12, 30), State.Throttle, "#4ADE80", "T");
                Pedal(dc, new Rect(cell.X + 32, cell.Y + 20, 12, 30), State.Brake, "#F87171", "B");
                break;
            default:
                Temp(dc, "WAT", State.WaterTempC, 105, cell, cell.Y + 28);
                Temp(dc, "OIL", State.OilTempC, 125, cell, cell.Y + 48);
                break;
        }
    }

    private void Pedal(DrawingContext dc, Rect track, double value, string color, string label)
    {
        dc.DrawRectangle(B("#2A2F35"), null, track);
        var h = track.Height * Math.Clamp(value, 0, 1);
        dc.DrawRectangle(B(color), null, new Rect(track.X, track.Bottom - h, track.Width, h));
        Text(dc, label, Regular, 9, B("#8A939C"), track.X + (track.Width / 2), track.Bottom + 2, HAlign.Center);
    }

    private void Temp(DrawingContext dc, string label, double? value, double warnAt, Rect cell, double y)
    {
        Text(dc, label, Regular, 9, B("#8A939C"), cell.X + 6, y, v: VAlign.Center);
        var text = value is { } v ? v.ToString("0", CultureInfo.InvariantCulture) : "—";
        var hot = value >= warnAt;
        Text(dc, text, Bold, 14, B(hot ? "#FBBF24" : "#E5E7EB"), cell.Right - 6, y, HAlign.Right, VAlign.Center);
    }
}
