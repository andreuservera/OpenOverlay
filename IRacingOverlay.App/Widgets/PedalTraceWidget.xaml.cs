using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class PedalTraceWidget : OverlayWindowBase
{
    private readonly PedalTracePanel _panel;

    public PedalTraceWidget() : base("PedalTrace", defaultLeft: 100, defaultTop: 700)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (PedalTracePanel)Scaler.ScalableContent!;
    }

    public void UpdateState(PedalTraceState state) => _panel.UpdateState(state);
}
