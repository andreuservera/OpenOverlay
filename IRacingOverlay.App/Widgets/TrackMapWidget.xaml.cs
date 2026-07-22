using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TrackMapWidget : OverlayWindowBase
{
    private readonly TrackMapPanel _panel;

    public TrackMapWidget() : base("TrackMap", defaultLeft: 300, defaultTop: 20)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (TrackMapPanel)Scaler.ScalableContent!;
    }

    public void UpdateState(IReadOnlyList<TrackMapMarker> markers) => _panel.UpdateState(markers);
}
