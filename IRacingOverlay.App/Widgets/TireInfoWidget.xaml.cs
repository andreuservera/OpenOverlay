using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TireInfoWidget : OverlayWindowBase
{
    public TireInfoWidget() : base("Tires", defaultLeft: 320, defaultTop: 420)
    {
        InitializeComponent();
        DataContext = this;
    }

    public void UpdateState(TireInfoState state) => Panel.UpdateState(state);
}
