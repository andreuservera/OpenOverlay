using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class DeltaWidget : OverlayWindowBase
{
    private readonly DeltaPanel _panel;

    public DeltaWidget() : base("Delta", defaultLeft: 560, defaultTop: 420)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (DeltaPanel)Scaler.ScalableContent!;
    }

    public void UpdateState(DeltaState state) => _panel.UpdateState(state);
}
