using System.Windows;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>
/// DEFAULT — a world-feed broadcast package, and the one theme that follows the app's own design system.
///
/// Philosophy: an official world-feed graphics package. Target: anyone; the best all-rounder.
/// Silhouette: a stepped lower-third — a tall light gear tile standing in front of two stacked
/// plates (a slim rev-strip plate over the main readout plate).
/// Typography: Bahnschrift semi-condensed, the app's broadcast face. Containers: layered plates with
/// hairline top highlights. Colour: graphite, with the one light tile as the anchor; state colours
/// only for state. Hierarchy: gear (inverted tile) → speed → RPM → ABS tag. Alerts: the gear tile
/// itself turns red at the shift point; ABS becomes an amber tag.
/// </summary>
public sealed class DefaultCockpit : CockpitDashboard
{
    private static readonly Typeface Numeric = Face("Bahnschrift SemiBold SemiCondensed", FontWeights.Normal);
    private static readonly Typeface GearFace = Face("Bahnschrift SemiBold Condensed", FontWeights.Normal);
    private static readonly Typeface Label = Face("Bahnschrift SemiBold SemiCondensed", FontWeights.Normal);
    private static readonly Brush Plate = Vertical("#F4161B22", "#F20D1015");
    private static readonly string[] LampColors = ["#34D399", "#FFD24D", "#F04438"];

    protected override Size DesignSize => new(372, 100);

    protected override void Draw(DrawingContext dc)
    {
        VerticalBand(dc, State.LeftProximity, new Rect(0, 4, 4, 92), B("#24FFFFFF"), B("#F5A524"), 2);
        VerticalBand(dc, State.RightProximity, new Rect(368, 4, 4, 92), B("#24FFFFFF"), B("#F5A524"), 2);

        // Gear tile: the inverted, light element the whole package hangs off.
        var shift = AtShiftPoint && ShiftLampsOn;
        dc.DrawRoundedRectangle(B(shift ? "#F04438" : "#F2F5F8"), null, new Rect(10, 0, 78, 100), 6, 6);
        Text(dc, Gear, GearFace, 72, B(shift ? "#FFFFFF" : "#0A0C0F"), 49, 50, HAlign.Center, VAlign.Center);
        Text(dc, "GEAR", Label, 10, B(shift ? "#FFE4E1" : "#5A6470"), 49, 84, HAlign.Center);

        // Rev strip plate.
        dc.DrawRoundedRectangle(B("#E60D1015"), P("#24FFFFFF", 1), new Rect(96, 0, 266, 22), 4, 4);
        for (var i = 0; i < LampCount; i++)
        {
            var color = IsLampLit(i) ? LampColors[LampStage(i)] : "#26FFFFFF";
            dc.DrawRoundedRectangle(B(color), null, new Rect(104 + (i * 18), 7, 15, 8), 1.5, 1.5);
        }

        // Main plate.
        dc.DrawRoundedRectangle(Plate, P("#2EFFFFFF", 1), new Rect(96, 28, 266, 72), 6, 6);
        var speed = Text(dc, Speed, Numeric, 44, B("#F2F5F8"), 190, 62, HAlign.Right, VAlign.Center);
        Text(dc, "KM/H", Label, 11, B("#8E99A5"), speed.Right + 5, 62 + (44 * 0.35), v: VAlign.Baseline);

        dc.DrawLine(P("#24FFFFFF", 1), new Point(240, 40), new Point(240, 88));
        Text(dc, Rpm, Numeric, 26, B("#C4CCD4"), 250, 56, v: VAlign.Center);
        Text(dc, "RPM", Label, 10, B("#8E99A5"), 250, 72);

        var absRect = new Rect(318, 54, 38, 20);
        if (AbsActive)
        {
            var amber = AbsFlashOn ? "#F5A524" : "#8A5D18";
            dc.DrawRoundedRectangle(B("#2EF5A524"), P(amber, 1), absRect, 3, 3);
            Text(dc, "ABS", Label, 11, B(amber), absRect.X + 19, absRect.Y + 10, HAlign.Center, VAlign.Center);
        }
        else
        {
            dc.DrawRoundedRectangle(B("#1CFFFFFF"), P("#14FFFFFF", 1), absRect, 3, 3);
            Text(dc, "ABS", Label, 11, B("#8E99A5"), absRect.X + 19, absRect.Y + 10, HAlign.Center, VAlign.Center);
        }
    }
}
