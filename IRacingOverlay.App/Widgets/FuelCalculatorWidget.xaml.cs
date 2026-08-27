using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class FuelCalculatorWidget : OverlayWindowBase
{
    private readonly FuelCalculatorPanel _panel;

    public FuelCalculatorWidget() : base("FuelCalculator", defaultLeft: 100, defaultTop: 380)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (FuelCalculatorPanel)Scaler.ScalableContent!;
    }

    public void UpdateState(FuelCalculatorState state) => _panel.UpdateState(state);

    public void SetOptions(FuelCalculatorOptions options) => _panel.Options = options;
}
