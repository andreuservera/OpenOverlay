using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class StandingsWidget : OverlayWindowBase
{
    public StandingsWidget() : base("Standings", defaultLeft: 420, defaultTop: 100)
    {
        InitializeComponent();
        DataContext = this;
    }

    public void UpdateRows(IReadOnlyList<StandingsRow> rows) => Panel.SetRows(rows);
}
