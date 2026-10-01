using System.Globalization;
using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>
/// PIT WALL — race-engineer software adapted for the driver.
///
/// Philosophy: be the driver and the engineer at once; every value exposed, labelled and gridded.
/// Target: data-driven drivers, setup tinkerers, league engineers. Silhouette: a long flat
/// instrument table — one row of labelled cells — bracketed by two vertical proximity bars, one
/// each side.
/// Typography: Tahoma — the dense UI face of classic telemetry software; small caps labels, bold
/// values. Containers: a strict grid with ruled cells and no rounding. Colour: workstation grey;
/// green/amber/red only on values that carry a state. Hierarchy: flat by design — every cell weighs
/// the same, the reading order is left to right. Alerts: the gear cell turns red at the shift point,
/// the ABS cell (the configured level) flashes amber while ABS intervenes, fuel and temperatures turn
/// amber when they need attention, and the side bars light amber where a car is alongside.
/// </summary>
public sealed class PitWallCockpit : CockpitDashboard
{
    private static readonly Typeface Bold = Face("Tahoma", FontWeights.Bold);
    private static readonly Typeface Regular = Face("Tahoma", FontWeights.Normal);
    private static readonly string[] LampColors = ["#4ADE80", "#FACC15", "#F87171"];
    private static readonly string[] LampOffColors = ["#1C3326", "#3A3314", "#3F1F22"];
    // Unit suffixes are appended at draw time: they follow iRacing's display units.
    private static readonly (string Label, double Width)[] Columns =
    [
        ("GEAR", 48), ("SPEED", 78), ("RPM", 70), ("SHIFT", 92), ("ABS", 52),
        ("FUEL", 74), ("INPUTS", 56), ("TEMPS", 72),
    ];

    private const double DashHeight = 68;
    private const double BarWidth = 8;
    private const double BarGap = 4;
    private const double TableX = BarWidth + BarGap;
    private static readonly double TableWidth = Columns.Sum(c => c.Width);
    private static readonly Dictionary<UnitSystem, string[]> ColumnLabels =
        Enum.GetValues<UnitSystem>().ToDictionary(units => units, units => Columns.Select((column, c) => c switch
        {
            1 => $"SPEED  {Units.SpeedUnit(units)}",
            7 => $"TEMPS  {Units.TemperatureUnit(units)}",
            _ => column.Label,
        }).ToArray());

    protected override Size DesignSize => new(TableWidth + (2 * TableX), DashHeight);

    protected override void Draw(DrawingContext dc)
    {
        var tableRight = TableX + TableWidth;
        VerticalBand(dc, State.LeftProximity, new Rect(0, 0, BarWidth, DashHeight), B("#E6101214"), B("#FBBF24"), 0);
        VerticalBand(dc, State.RightProximity, new Rect(tableRight + BarGap, 0, BarWidth, DashHeight), B("#E6101214"), B("#FBBF24"), 0);

        dc.DrawRectangle(B("#F2101214"), P("#3A3F45", 1), new Rect(TableX + 0.5, 0.5, TableWidth - 1, DashHeight - 1));

        var labels = ColumnLabels[State.UnitSystem];
        var x = TableX;
        for (var c = 0; c < Columns.Length; c++)
        {
            var cell = new Rect(x, 1, Columns[c].Width, DashHeight - 1);
            if (c > 0)
            {
                dc.DrawLine(P("#2A2F35", 1), new Point(x + 0.5, 0), new Point(x + 0.5, DashHeight));
            }

            DrawCell(dc, c, cell);
            CachedText(dc, labels[c], Regular, 9, B("#8A939C"), cell.X + 6, cell.Y + 5);
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
                // One row of segments grouped by stage, stepping up in height like a bar-graph tach.
                const double stageGap = 4;
                var w = (cell.Width - 12 - (LampCount - 1) - (2 * (stageGap - 1))) / LampCount;
                var baseline = cell.Y + 58;
                var lx = cell.X + 6;
                for (var i = 0; i < LampCount; i++)
                {
                    if (i > 0)
                    {
                        lx += LampStage(i) != LampStage(i - 1) ? stageGap : 1;
                    }

                    var h = 14 + (20.0 * i / (LampCount - 1));
                    var color = IsLampLit(i) ? LampColors[LampStage(i)] : LampOffColors[LampStage(i)];
                    dc.DrawRectangle(B(color), null, new Rect(lx, baseline - h, w, h));
                    lx += w;
                }

                dc.DrawLine(P("#3A3F45", 1), new Point(cell.X + 6, baseline + 1.5), new Point(cell.Right - 6, baseline + 1.5));
                break;
            case 4:
                if (AbsActive)
                {
                    dc.DrawRectangle(B("#3A2E0E"), null, new Rect(cell.X + 1, cell.Y, cell.Width - 1, cell.Height - 1));
                }

                var absColor = AbsActive ? (AbsFlashOn ? "#FBBF24" : "#8A6A1E") : "#E5E7EB";
                Text(dc, AbsLevel, Bold, 18, B(absColor), right, middle, HAlign.Right, VAlign.Center);
                break;
            case 5:
                var low = State.FuelPct is < 0.1;
                var fuel = CachedText(dc, FuelUnit, Regular, 10, B("#8A939C"), right, cell.Y + 30, HAlign.Right, VAlign.Center);
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
        CachedText(dc, label, Regular, 9, B("#8A939C"), track.X + (track.Width / 2), track.Bottom + 2, HAlign.Center);
    }

    // Warning thresholds stay in °C — the telemetry's own unit — and only the text is converted.
    private void Temp(DrawingContext dc, string label, double? celsius, double warnAtCelsius, Rect cell, double y)
    {
        CachedText(dc, label, Regular, 9, B("#8A939C"), cell.X + 6, y, v: VAlign.Center);
        var hot = celsius >= warnAtCelsius;
        Text(dc, Temperature(celsius), Bold, 14, B(hot ? "#FBBF24" : "#E5E7EB"), cell.Right - 6, y, HAlign.Right, VAlign.Center);
    }
}
