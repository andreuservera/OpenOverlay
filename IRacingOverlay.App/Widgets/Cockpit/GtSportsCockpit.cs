using System.Windows;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>
/// GT SPORTS — a modern steering-wheel display, expanded into a HUD.
///
/// Philosophy: designed by race engineers; every pixel is a module. Target: open-wheel drivers who
/// think in wheel pages. Silhouette: a wheel face — a trapezoid bezel with a rail of round LEDs,
/// flanked by two grips that carry the proximity LEDs.
/// Typography: Consolas — monospaced engineering labels ("SPD", "RPM", "FUEL"). Containers: outlined
/// modules on a black screen, each outlined in its own code colour. Colour: the F1 wheel convention
/// (green, red, blue LEDs) and colour-coded module outlines. Hierarchy: gear module (tallest, centre)
/// → speed / RPM modules → status modules. Alerts: the gear module fills red at the shift point; the
/// ABS module (which shows the configured level) turns amber and the two yellow lamps each side of
/// the rail flash while ABS intervenes; side LEDs on the grips light for cars alongside.
/// </summary>
public sealed class GtSportsCockpit : CockpitDashboard
{
    private static readonly Typeface Mono = Face("Consolas", FontWeights.Bold);
    private static readonly Typeface MonoLight = Face("Consolas", FontWeights.Normal);
    private static readonly string[] LampColors = ["#22C55E", "#EF4444", "#3B82F6"];
    private static readonly Geometry Bezel = Polygon(new(12, 0), new(328, 0), new(310, 158), new(30, 158));
    private static readonly Transform Inset = CreateInset();
    private const int GripLedCount = 6;

    private static Transform CreateInset()
    {
        var inset = new TranslateTransform(14, 0);
        inset.Freeze();
        return inset;
    }

    protected override Size DesignSize => new(368, 176);

    protected override void Draw(DrawingContext dc)
    {
        // Everything below is laid out 14 px in, so the bezel can grow outward to fit the ABS lamps.
        dc.PushTransform(Inset);

        // Grips, with the proximity LEDs down each one.
        dc.DrawRoundedRectangle(B("#15171A"), P("#26292E", 1), new Rect(-14, 44, 40, 132), 18, 18);
        dc.DrawRoundedRectangle(B("#15171A"), P("#26292E", 1), new Rect(314, 44, 40, 132), 18, 18);
        GripLeds(dc, State.LeftProximity, 6);
        GripLeds(dc, State.RightProximity, 334);

        dc.DrawGeometry(B("#1B1E22"), P("#2E333A", 1.5), Bezel);

        for (var i = 0; i < LampCount; i++)
        {
            var center = new Point(66 + (i * 16), 16);
            dc.DrawEllipse(B(IsLampLit(i) ? LampColors[LampStage(i)] : "#26292E"), P("#0B0C0E", 1.5), center, 6.5, 6.5);
        }

        var absLamp = B(AbsActive ? (AbsFlashOn ? "#FACC15" : "#8A6A1E") : "#26292E");
        foreach (var x in (double[])[26, 42, 298, 314])
        {
            dc.DrawEllipse(absLamp, P("#0B0C0E", 1.5), new Point(x, 16), 6.5, 6.5);
        }

        dc.DrawRoundedRectangle(B("#05070A"), P("#3A4048", 1), new Rect(64, 32, 212, 116), 6, 6);

        Module(dc, new Rect(70, 38, 62, 62), "#22D3EE", "SPD");
        Text(dc, Speed, Mono, 26, B("#FFFFFF"), 128, 70, HAlign.Right, VAlign.Center);
        Text(dc, SpeedUnit == "mph" ? "MPH" : "KPH", MonoLight, 9, B("#6B7280"), 128, 88, HAlign.Right);

        var shift = AtShiftPoint && ShiftLampsOn;
        var gearRect = new Rect(138, 38, 64, 104);
        dc.DrawRoundedRectangle(shift ? B("#EF4444") : null, P("#E5E7EB", 1.5), gearRect, 3, 3);
        Text(dc, "GEAR", MonoLight, 9, B(shift ? "#FFFFFF" : "#9CA3AF"), gearRect.X + 5, gearRect.Y + 4);
        Text(dc, Gear, Mono, 64, B("#FFFFFF"), 170, 92, HAlign.Center, VAlign.Center);

        Module(dc, new Rect(208, 38, 62, 62), "#A3E635", "RPM");
        Text(dc, Rpm, Mono, 18, B("#FFFFFF"), 266, 72, HAlign.Right, VAlign.Center);

        var absRect = new Rect(70, 106, 62, 36);
        var absColor = AbsActive ? (AbsFlashOn ? "#F5A524" : "#8A5D18") : "#4B5563";
        dc.DrawRoundedRectangle(AbsActive ? B("#26F5A524") : null, P(absColor, 1.5), absRect, 3, 3);
        Text(dc, "ABS", MonoLight, 9, B(AbsActive ? absColor : "#6B7280"), absRect.X + 5, absRect.Y + 4);
        Text(dc, AbsLevel, Mono, 14, B(AbsActive ? absColor : "#FFFFFF"), absRect.Right - 5, absRect.Y + 24, HAlign.Right, VAlign.Center);

        Module(dc, new Rect(208, 106, 62, 36), "#8B5CF6", "FUEL");
        var fuelUnit = Text(dc, FuelUnit.ToUpperInvariant(), MonoLight, 9, B("#6B7280"), 266, 130, HAlign.Right, VAlign.Center);
        Text(dc, Fuel, Mono, 14, B("#FFFFFF"), fuelUnit.X - 3, 130, HAlign.Right, VAlign.Center);

        dc.Pop();
    }

    private void Module(DrawingContext dc, Rect rect, string color, string label)
    {
        dc.DrawRoundedRectangle(null, P(color, 1.5), rect, 3, 3);
        Text(dc, label, MonoLight, 9, B(color), rect.X + 5, rect.Y + 4);
    }

    private static void GripLeds(DrawingContext dc, ViewModels.ProximitySide side, double x)
    {
        for (var i = 0; i < GripLedCount; i++)
        {
            var from = (double)i / GripLedCount;
            var to = (double)(i + 1) / GripLedCount;
            var lit = HasCar(side) && from < side.BandEnd && to > side.BandStart;
            dc.DrawEllipse(B(lit ? "#F59E0B" : "#2A2D31"), P("#0B0C0E", 1), new Point(x, 70 + (i * 18)), 5, 5);
        }
    }
}
