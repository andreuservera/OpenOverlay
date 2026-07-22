using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TrackInfoWidget : OverlayWindowBase
{
    private readonly TrackInfoPanel _panel;

    public TrackInfoWidget() : base("TrackInfo", defaultLeft: 420, defaultTop: 20)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (TrackInfoPanel)Scaler.ScalableContent!;
    }

    public void UpdateState(TrackInfoState state) => _panel.UpdateState(state);
}
