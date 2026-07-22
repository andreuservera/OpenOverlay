using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class RelativeWidget : OverlayWindowBase
{
    private readonly RelativePanel _panel;

    public RelativeWidget() : base("Relative", defaultLeft: 100, defaultTop: 100)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (RelativePanel)Scaler.ScalableContent!;
    }

    public void UpdateRows(IReadOnlyList<RelativeRow> rows) => _panel.SetRows(rows);
}
