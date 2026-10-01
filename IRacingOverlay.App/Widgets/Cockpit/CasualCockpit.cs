using System.Windows;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>
/// CASUAL — big, bold GT-style numbers without the noise.
///
/// Philosophy: large race-critical numbers you can read without looking. Target: drivers who want a
/// simple, confident dash. Silhouette: one hard parallelogram leaning forward — the entire
/// dashboard is drawn through a skew, so every block, bar and numeral is italicised by it.
/// Typography: Impact, everywhere. Containers: hard-edged graphite slabs, no rounding at all.
/// Colour: deliberately calm — graphite and white, one thin red accent, muted rev colours. Red only
/// floods the gear slab at the shift point. Hierarchy: gear → speed (huge) → rev blocks → RPM.
/// Alerts: the gear slab turns red at the shift point; ABS is an amber-outlined tag.
/// </summary>
public sealed class CasualCockpit : CockpitDashboard
{
    private const double Lean = 31; // horizontal offset across the full height
    private static readonly Typeface Impact = Face("Impact", FontWeights.Normal);
    private static readonly string[] LampColors = ["#3FA66A", "#C9A43B", "#C8443C"];
    private static readonly Transform Skew = CreateSkew();

    protected override Size DesignSize => new(371, 124);

    protected override void Draw(DrawingContext dc)
    {
        dc.PushTransform(Skew);

        VerticalBand(dc, State.LeftProximity, new Rect(0, 4, 5, 116), B("#1F2125"), B("#E0A030"), 0);
        VerticalBand(dc, State.RightProximity, new Rect(335, 4, 5, 116), B("#1F2125"), B("#E0A030"), 0);

        dc.DrawRectangle(B("#EE111316"), null, new Rect(10, 0, 319, 124));
        dc.DrawRectangle(B("#C8443C"), null, new Rect(10, 0, 3, 124));

        var shift = AtShiftPoint && ShiftLampsOn;
        dc.DrawRectangle(B(shift ? "#C8443C" : "#1C1F24"), null, new Rect(22, 0, 86, 124));
        Text(dc, Gear, Impact, 96, B("#F2F5F8"), 65, 62, HAlign.Center, VAlign.Center);

        var speed = Text(dc, Speed, Impact, 62, B("#F2F5F8"), 232, 52, HAlign.Right, VAlign.Center);
        Text(dc, SpeedUnit.ToUpperInvariant(), Impact, 13, B("#7C8590"), speed.Right + 6, 52 + (62 * 0.35), v: VAlign.Baseline);

        Text(dc, Rpm, Impact, 26, B("#9CA3AF"), 320, 22, HAlign.Right, VAlign.Center);
        Text(dc, "RPM", Impact, 11, B("#6B7280"), 320, 36, HAlign.Right);

        // Rev blocks grow taller toward the limiter.
        for (var i = 0; i < LampCount; i++)
        {
            var height = 12 + (i * 1.1);
            var color = IsLampLit(i) ? LampColors[LampStage(i)] : "#202328";
            dc.DrawRectangle(B(color), null, new Rect(118 + (i * 14.6), 114 - height, 12.6, height));
        }

        var absRect = new Rect(118, 6, 46, 18);
        if (AbsActive)
        {
            var amber = AbsFlashOn ? "#E0A030" : "#7A5A20";
            dc.DrawRectangle(B("#26E0A030"), P(amber, 1.5), absRect);
            Text(dc, AbsLabel, Impact, 13, B(amber), absRect.X + 23, absRect.Y + 9, HAlign.Center, VAlign.Center);
        }
        else
        {
            dc.DrawRectangle(null, P("#3A3D42", 1.5), absRect);
            Text(dc, AbsLabel, Impact, 13, B("#6B7280"), absRect.X + 23, absRect.Y + 9, HAlign.Center, VAlign.Center);
        }

        dc.Pop();
    }

    private static Transform CreateSkew()
    {
        // x' = x + Lean - y * Lean / height: the top edge leans right, the bottom sits at x = 0.
        var transform = new MatrixTransform(new Matrix(1, 0, -Lean / 124, 1, Lean, 0));
        transform.Freeze();
        return transform;
    }
}
