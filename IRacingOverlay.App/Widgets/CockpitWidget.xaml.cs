using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class CockpitWidget : OverlayWindowBase
{
    public CockpitWidget() : base("Cockpit", defaultLeft: 740, defaultTop: 700)
    {
        InitializeComponent();
        DataContext = this;
    }

    public void UpdateState(CockpitState state) => Panel.UpdateState(state);
}
