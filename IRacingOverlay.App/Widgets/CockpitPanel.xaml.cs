using System.Globalization;
using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class CockpitPanel : UserControl
{
    private bool _blinkPhase;

    public CockpitPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(CockpitState state)
    {
        _blinkPhase = !_blinkPhase;

        ShiftLights.SetLit(state.ShiftLightsLit, state.ShiftBlink, _blinkPhase);
        ShiftGear.SetGear(state.Gear);
        AbsIndicator.SetActive(state.AbsActive, _blinkPhase);
        SpeedText.Text = state.SpeedKph > 0 ? state.SpeedKph.ToString("0", CultureInfo.InvariantCulture) : "—";
        RpmText.Text = state.Rpm > 0 ? state.Rpm.ToString("0", CultureInfo.InvariantCulture) : "—";
        LeftProximity.SetBand(state.LeftProximity.BandStart, state.LeftProximity.BandEnd);
        RightProximity.SetBand(state.RightProximity.BandStart, state.RightProximity.BandEnd);
    }
}
