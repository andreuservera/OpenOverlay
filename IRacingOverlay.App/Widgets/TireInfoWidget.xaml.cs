using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TireInfoWidget : OverlayWindowBase
{
    private readonly TireInfoPanel _panel;

    public TireInfoWidget() : base("Tires", defaultLeft: 320, defaultTop: 420)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (TireInfoPanel)Scaler.ScalableContent!;
    }

    public void UpdateState(TireInfoState state) => _panel.UpdateState(state);
}
