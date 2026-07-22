using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class CockpitWidget : OverlayWindowBase
{
    private readonly CockpitPanel _panel;

    public CockpitWidget() : base("Cockpit", defaultLeft: 740, defaultTop: 700)
    {
        InitializeComponent();
        DataContext = this;
        var viewbox = (System.Windows.Controls.Viewbox)Scaler.ScalableContent!;
        var grid = (System.Windows.Controls.Grid)viewbox.Child;
        _panel = (CockpitPanel)grid.Children[0];
    }

    // Matches the inner Grid's fixed 310x150 above — measured as CockpitPanel's own natural size
    // (Auto-sized columns, zero slack) rather than guessed, so the design only reads correctly at
    // this exact proportion. Dragging the resize grip grows/shrinks both dimensions together
    // instead of letting the aspect ratio drift.
    protected override double? FixedAspectRatio => 310.0 / 150.0;

    public void UpdateState(CockpitState state) => _panel.UpdateState(state);
}
