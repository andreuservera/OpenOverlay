using System.Windows;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>
/// CLASSIC CAR — classic instrumentation reimagined for sim racing.
///
/// Philosophy: a legendary machine's rev counter, readable at speed. Target: historic, classic and
/// GT-heritage drivers who want character. Silhouette: a round gauge in a chrome bezel, with two
/// slim indicator lamps standing either side of it.
/// Typography: Franklin Gothic numerals on the dial, Georgia italic for the maker-style captions.
/// Containers: turned-metal bezel, black face, a cream odometer window. Colour: cream on black,
/// chrome, a red needle; warm jewel lamps. Hierarchy: the needle (RPM) first — the instrument is a
/// tachometer — then gear window, odometer speed, jewels. Alerts: the shift jewels arc across the top
/// of the face and strobe at the shift point; an amber ABS tell-tale lights beside the hub.
/// </summary>
public sealed class ClassicCarCockpit : CockpitDashboard
{
    private const double StartAngle = -135;
    private const double Sweep = 270;
    private static readonly Point Center = new(128, 112);
    private static readonly Typeface Dial = Face("Franklin Gothic Medium", FontWeights.Normal);
    private static readonly Typeface Caption = Face("Georgia", FontWeights.Normal, FontStyles.Italic);
    private static readonly Brush Chrome = CreateChrome();
    private static readonly Brush DialFace = CreateFace();
    private static readonly string[] LampColors = ["#65A30D", "#F59E0B", "#DC2626"];

    // The dial's scale grows to fit the engine it is fitted to, in whole thousands, like choosing
    // the right gauge face for the car. It never shrinks mid-session.
    private double _scaleMax = 8000;

    protected override Size DesignSize => new(256, 224);

    // A period rev counter has no flashing shift light; the jewels just stay lit.
    protected override bool FlashesAtShiftPoint => false;

    protected override void Draw(DrawingContext dc)
    {
        if (State.Rpm + 250 > _scaleMax)
        {
            _scaleMax = Math.Ceiling((State.Rpm + 250) / 1000) * 1000;
        }

        VerticalBand(dc, State.LeftProximity, new Rect(0, 42, 8, 140), B("#2A2520"), B("#F59E0B"), 4);
        VerticalBand(dc, State.RightProximity, new Rect(248, 42, 8, 140), B("#2A2520"), B("#F59E0B"), 4);

        dc.DrawEllipse(Chrome, P("#2A2D31", 1), Center, 110, 110);
        dc.DrawEllipse(DialFace, null, Center, 102, 102);

        var thousands = (int)(_scaleMax / 1000);
        for (var half = 0; half <= thousands * 2; half++)
        {
            var angle = AngleOf(half * 500);
            var major = half % 2 == 0;
            dc.DrawLine(P("#EDE3C8", major ? 2 : 1), OnCircle(Center, major ? 90 : 95, angle), OnCircle(Center, 101, angle));
            if (major)
            {
                var label = OnCircle(Center, 78, angle);
                Text(dc, (half / 2).ToString(), Dial, 13, B("#EDE3C8"), label.X, label.Y, HAlign.Center, VAlign.Center);
            }
        }

        for (var i = 0; i < LampCount; i++)
        {
            var at = OnCircle(Center, 60, -58 + (i * (116.0 / (LampCount - 1))));
            dc.DrawEllipse(B(IsLampLit(i) ? LampColors[LampStage(i)] : "#2B2723"), P("#5A5046", 1), at, 4.5, 4.5);
        }

        var gearAt = new Point(Center.X, 84);
        var shift = AtShiftPoint && ShiftLampsOn;
        dc.DrawEllipse(B(shift ? "#7F1D1D" : "#0B0B0B"), P("#8C7B5A", 1.5), gearAt, 15, 15);
        Text(dc, Gear, Dial, 22, B("#EDE3C8"), gearAt.X, gearAt.Y, HAlign.Center, VAlign.Center);

        var odometer = new Rect(98, 146, 60, 22);
        dc.DrawRectangle(B("#EDE3C8"), P("#8C7B5A", 1), odometer);
        Text(dc, Speed, Dial, 18, B("#141414"), Center.X, odometer.Y + 11, HAlign.Center, VAlign.Center);
        Text(dc, $"{SpeedUnit} · {Rpm} rpm", Caption, 8.5, B("#B8AC8E"), Center.X, 172, HAlign.Center);

        var absAt = new Point(84, 126);
        dc.DrawEllipse(B(AbsActive ? (AbsFlashOn ? "#F59E0B" : "#8A5D18") : "#3A2A12"), P("#5A5046", 1), absAt, 6, 6);
        Text(dc, AbsLabel, Caption, 8.5, B(AbsActive ? "#F59E0B" : "#8C7B5A"), absAt.X, absAt.Y + 9, HAlign.Center);

        var needle = AngleOf(Math.Min(State.Rpm, _scaleMax));
        dc.DrawLine(P("#E03131", 3), OnCircle(Center, -12, needle), OnCircle(Center, 88, needle));
        dc.DrawEllipse(B("#C7CBD0"), P("#5E6368", 1), Center, 8, 8);
    }

    private double AngleOf(double rpm) => StartAngle + (Sweep * rpm / _scaleMax);

    private static Brush CreateChrome()
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.35, 0.25),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xEE, 0xF0, 0xF2), 0),
                new GradientStop(Color.FromRgb(0x8C, 0x91, 0x96), 0.7),
                new GradientStop(Color.FromRgb(0x3B, 0x3F, 0x44), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    private static Brush CreateFace()
    {
        var brush = new RadialGradientBrush(Color.FromRgb(0x1E, 0x1E, 0x1E), Color.FromRgb(0x0A, 0x0A, 0x0A));
        brush.Freeze();
        return brush;
    }
}
