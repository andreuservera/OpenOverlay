using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class RelativeWidget : OverlayWindowBase
{
    public RelativeWidget() : base("Relative")
    {
        InitializeComponent();
        DataContext = this;
    }

    public void UpdateRows(IReadOnlyList<RelativeRow> rows) => Panel.SetRows(rows);
}
