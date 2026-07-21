using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class RelativeWidget : OverlayWindowBase
{
    public RelativeWidget() : base("Relative", defaultLeft: 100, defaultTop: 100)
    {
        InitializeComponent();
        DataContext = this;
    }

    public void UpdateRows(IReadOnlyList<RelativeRow> rows) => Panel.SetRows(rows);
}
