using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class CockpitWidget : OverlayWindowBase
{
    private readonly CockpitPanel _panel;

    public CockpitWidget() : base("Cockpit", defaultLeft: 740, defaultTop: 700)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (CockpitPanel)Scaler.ScalableContent!;
    }

    public void UpdateState(CockpitState state) => _panel.UpdateState(state);
}
