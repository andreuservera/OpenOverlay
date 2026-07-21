using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class DeltaWidget : OverlayWindowBase
{
    public DeltaWidget() : base("Delta", defaultLeft: 560, defaultTop: 420)
    {
        InitializeComponent();
        DataContext = this;
    }

    public void UpdateState(DeltaState state) => Panel.UpdateState(state);
}
