using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class FlagWidget : OverlayWindowBase
{
    public FlagWidget() : base("Flag", defaultLeft: 100, defaultTop: 420)
    {
        InitializeComponent();
        DataContext = this;
    }

    public void UpdateState(FlagState state) => Panel.UpdateState(state);
}
