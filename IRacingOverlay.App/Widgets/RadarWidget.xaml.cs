using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class RadarWidget : OverlayWindowBase
{
    private readonly RadarPanel _panel;

    public RadarWidget() : base("Radar", defaultLeft: 900, defaultTop: 500)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (RadarPanel)Scaler.ScalableContent!;
    }

    public void UpdateState(RadarState state) => _panel.UpdateState(state);
}
