using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class IncidentWidget : OverlayWindowBase
{
    private readonly IncidentPanel _panel;

    public IncidentWidget() : base("Incident", defaultLeft: 800, defaultTop: 560)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (IncidentPanel)Scaler.ScalableContent!;
    }

    public void UpdateState(IncidentState state) => _panel.UpdateState(state);
}
