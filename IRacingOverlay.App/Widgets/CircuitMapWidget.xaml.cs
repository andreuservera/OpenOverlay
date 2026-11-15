using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.TrackMaps;

namespace IRacingOverlay.App.Widgets;

public partial class CircuitMapWidget : OverlayWindowBase
{
    private readonly CircuitMapPanel _panel;

    public CircuitMapWidget() : base("CircuitMap", defaultLeft: 40, defaultTop: 600)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (CircuitMapPanel)Scaler.ScalableContent!;
    }

    public void UpdateState(CircuitMapState state) => _panel.UpdateState(state);
}
