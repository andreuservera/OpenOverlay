using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class FuelWidget : OverlayWindowBase
{
    private readonly FuelPanel _panel;

    public FuelWidget() : base("Fuel", defaultLeft: 800, defaultTop: 420)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (FuelPanel)Scaler.ScalableContent!;
    }

    public void UpdateState(FuelState state) => _panel.UpdateState(state);
}
