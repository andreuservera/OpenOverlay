using System;
using System.Globalization;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class CockpitPanel : UserControl
{
    // Half-period of the ABS blink, in milliseconds — a fixed wall-clock cadence rather than "flip
    // once per update," so the blink rate stays the same regardless of how fast the critical refresh
    // rate (Cockpit's own update timer) is set. Toggling once per tick used to look like a genuine
    // blink at the old 10Hz default (100ms => a 5Hz blink) but turned into a ~30Hz flicker/stutter
    // once the refresh rate was raised toward 60Hz.
    private const long BlinkHalfPeriodMs = 150;

    public CockpitPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(CockpitState state)
    {
        var blinkPhase = (Environment.TickCount64 / BlinkHalfPeriodMs) % 2 == 0;

        // The shift lights run their own animation clock instead of taking blinkPhase — see
        // ShiftLightsPanel.
        ShiftLights.SetLit(state.ShiftLightsLit, state.ShiftBlink);
        ShiftGear.SetGear(state.Gear);
        AbsIndicator.SetActive(state.AbsActive, blinkPhase);
        SpeedText.Text = state.SpeedKph > 0 ? state.SpeedKph.ToString("0", CultureInfo.InvariantCulture) : "—";
        RpmText.Text = state.Rpm > 0 ? state.Rpm.ToString("0", CultureInfo.InvariantCulture) : "—";
        LeftProximity.SetBand(state.LeftProximity.BandStart, state.LeftProximity.BandEnd);
        RightProximity.SetBand(state.RightProximity.BandStart, state.RightProximity.BandEnd);
    }
}
