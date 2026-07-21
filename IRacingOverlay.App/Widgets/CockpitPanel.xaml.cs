using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class CockpitPanel : UserControl
{
    private static readonly Color AbsIdle = Color.FromRgb(0x2E, 0x1E, 0x1E);
    private static readonly Color AbsDim = Color.FromRgb(0x80, 0x18, 0x18);
    private static readonly Color AbsBright = Color.FromRgb(0xFF, 0x30, 0x30);

    private static readonly Color TcIdle = Color.FromRgb(0x1E, 0x2E, 0x1E);
    private static readonly Color TcDim = Color.FromRgb(0x18, 0x80, 0x18);
    private static readonly Color TcBright = Color.FromRgb(0x30, 0xFF, 0x30);

    private bool _blinkPhase;

    public CockpitPanel()
    {
        InitializeComponent();
        AbsBar.Configure(AbsIdle, AbsDim, AbsBright);
        TcBar.Configure(TcIdle, TcDim, TcBright);
    }

    public void UpdateState(CockpitState state)
    {
        _blinkPhase = !_blinkPhase;

        ShiftGear.UpdateState(state.Gear, state.ShiftLightsLit, state.ShiftBlink, _blinkPhase);
        AbsBar.SetActive(state.AbsActive, _blinkPhase);
        TcBar.SetActive(state.TcActive, _blinkPhase);
        LeftProximity.SetFraction(state.LeftProximity);
        RightProximity.SetFraction(state.RightProximity);
    }
}
