using System.Windows;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>
/// HYPERCAR — a next-generation endurance interface.
///
/// Philosophy: a prototype from the future; light, calm, precise. Target: endurance and prototype
/// drivers doing long stints, who want refinement over aggression. Silhouette: three floating glass
/// objects with air between them — a speed pill, a gear orb ringed by the revs, an RPM pill.
/// Typography: Segoe UI Light — thin, wide, lowercase units. Containers: translucent glass with a
/// single light rim; nothing opaque. Colour: teal → ice → rose, desaturated. Hierarchy: the orb
/// (gear + ring) is the object; the pills are satellites. Alerts: the ring's last arc blooms rose at
/// the shift point; ABS glows amber inside the orb; cars alongside light arcs hugging the orb.
/// </summary>
public sealed class HypercarCockpit : CockpitDashboard
{
    private static readonly Point Center = new(186, 78);
    private static readonly Typeface Light = Face("Segoe UI Light", FontWeights.Normal);
    private static readonly Typeface Regular = Face("Segoe UI", FontWeights.Normal);
    private static readonly Typeface Semibold = Face("Segoe UI Semibold", FontWeights.Normal);
    private static readonly string[] LampColors = ["#5EEAD4", "#E0E7FF", "#FB7185"];

    protected override Size DesignSize => new(372, 156);

    protected override void Draw(DrawingContext dc)
    {
        Proximity(dc, State.LeftProximity, 320, -100);
        Proximity(dc, State.RightProximity, 40, 100);

        dc.DrawEllipse(B("#1AFFFFFF"), P("#40FFFFFF", 1), Center, 60, 60);

        const double sweep = 240;
        const double gap = 3;
        var span = (sweep - (gap * (LampCount - 1))) / LampCount;
        for (var i = 0; i < LampCount; i++)
        {
            var from = -120 + (i * (span + gap));
            var color = IsLampLit(i) ? LampColors[LampStage(i)] : "#1FFFFFFF";
            dc.DrawGeometry(B(color), null, ArcBand(Center, 66, 72, from, from + span));
        }

        Text(dc, Gear, Light, 64, B("#FFFFFF"), Center.X, 72, HAlign.Center, VAlign.Center);
        Text(dc, "G E A R", Regular, 8.5, B("#80FFFFFF"), Center.X, 100, HAlign.Center);

        var absColor = AbsActive ? (AbsFlashOn ? "#FBBF24" : "#8A6A1E") : "#59FFFFFF";
        if (AbsActive)
        {
            dc.DrawEllipse(B(absColor), null, new Point(Center.X - 16, 122), 2.5, 2.5);
        }

        Text(dc, "ABS", Semibold, 9, B(absColor), Center.X + (AbsActive ? 4 : 0), 122, HAlign.Center, VAlign.Center);

        Pill(dc, new Rect(0, 46, 92, 64), Speed, SpeedUnit, 34);
        Pill(dc, new Rect(280, 46, 92, 64), Rpm, "rpm", 24);
    }

    private void Pill(DrawingContext dc, Rect rect, string value, string unit, double size)
    {
        dc.DrawRoundedRectangle(B("#14FFFFFF"), P("#33FFFFFF", 1), rect, rect.Height / 2, rect.Height / 2);
        Text(dc, value, Light, size, B("#FFFFFF"), rect.X + (rect.Width / 2), rect.Y + 27, HAlign.Center, VAlign.Center);
        Text(dc, unit, Regular, 10, B("#99FFFFFF"), rect.X + (rect.Width / 2), rect.Y + 44, HAlign.Center);
    }

    // Arcs hugging the orb: front of the car at the top of the arc, rear at the bottom.
    private static void Proximity(DrawingContext dc, ViewModels.ProximitySide side, double topAngle, double sweep)
    {
        var (from, to) = sweep > 0 ? (topAngle, topAngle + sweep) : (topAngle + sweep, topAngle);
        dc.DrawGeometry(B("#14FFFFFF"), null, ArcBand(Center, 80, 83, from, to));
        if (HasCar(side))
        {
            var a = topAngle + (sweep * side.BandStart);
            var b = topAngle + (sweep * side.BandEnd);
            dc.DrawGeometry(B("#FBBF24"), null, ArcBand(Center, 79, 84, Math.Min(a, b), Math.Max(a, b)));
        }
    }
}
