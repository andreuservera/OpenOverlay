using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace IRacingOverlay.App.Widgets.Cockpit;

/// <summary>
/// INVISIBLE — the dashboard that almost isn't there.
///
/// Philosophy: everything needed, nothing else; maximum track visibility. Target: VR drivers and
/// purists who find overlays distracting. Silhouette: none — a floating rail of rev lights over a
/// centred gear, speed and RPM either side. No container: a soft shadow keeps it legible over a
/// bright track.
/// Typography: Segoe UI Variable Display — clean, tabular figures, no outlines. Colour: white at
/// descending opacity; colour only on the rev lights and when something needs attention.
/// Hierarchy: gear → speed → RPM → lights. Alerts: the rev rail flashes and the gear turns red at the
/// shift point; ABS and the side bars only exist while they have something to say.
/// </summary>
public sealed class InvisibleCockpit : CockpitDashboard
{
    private static readonly FontFamily Family = new("Segoe UI Variable Display, Segoe UI");
    private static readonly Typeface Semibold = new(Family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Regular = new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly string[] LampColors = ["#4ADE80", "#FACC15", "#F43F5E"];

    private const double DashWidth = 276;
    private const double Center = DashWidth / 2;
    private const double PillWidth = 14;
    private const double PillGap = 4;

    public InvisibleCockpit()
    {
        var shadow = new DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Direction = 270, Opacity = 0.8,
            RenderingBias = RenderingBias.Performance,
        };
        shadow.Freeze();
        Effect = shadow;
    }

    protected override Size DesignSize => new(DashWidth, 96);

    protected override void Draw(DrawingContext dc)
    {
        var railWidth = (LampCount * PillWidth) + ((LampCount - 1) * PillGap);
        var railX = Center - (railWidth / 2);
        for (var i = 0; i < LampCount; i++)
        {
            var color = IsLampLit(i) ? LampColors[LampStage(i)] : "#40FFFFFF";
            dc.DrawRoundedRectangle(B(color), null, new Rect(railX + (i * (PillWidth + PillGap)), 6, PillWidth, 5), 2.5, 2.5);
        }

        var shift = AtShiftPoint && ShiftLampsOn;
        Text(dc, Gear, Semibold, 56, B(shift ? "#F43F5E" : "#FFFFFF"), Center, 72, HAlign.Center, VAlign.Baseline);

        Text(dc, Speed, Semibold, 30, B("#FFFFFF"), Center - 34, 62, HAlign.Right, VAlign.Baseline);
        Text(dc, "KM/H", Semibold, 9, B("#A6FFFFFF"), Center - 35, 66, HAlign.Right);

        Text(dc, Rpm, Regular, 30, B("#D9FFFFFF"), Center + 34, 62, v: VAlign.Baseline);
        Text(dc, "RPM", Semibold, 9, B("#A6FFFFFF"), Center + 35, 66);

        if (AbsActive)
        {
            var amber = AbsFlashOn ? "#FBBF24" : "#9A7418";
            var pill = new Rect(Center - 18, 80, 36, 14);
            dc.DrawRoundedRectangle(B("#33FBBF24"), P(amber, 1), pill, 7, 7);
            Text(dc, "ABS", Semibold, 9, B(amber), Center, pill.Y + 7, HAlign.Center, VAlign.Center);
        }

        SideBar(dc, State.LeftProximity, 2);
        SideBar(dc, State.RightProximity, DashWidth - 6);
    }

    private static void SideBar(DrawingContext dc, ViewModels.ProximitySide side, double x)
    {
        if (HasCar(side))
        {
            VerticalBand(dc, side, new Rect(x, 16, 4, 76), Brushes.Transparent, B("#FBBF24"), 2);
        }
    }
}
