using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class StandingsWidget : OverlayWindowBase
{
    private readonly StandingsPanel _panel;

    public StandingsWidget() : base("Standings", defaultLeft: 420, defaultTop: 100)
    {
        InitializeComponent();
        DataContext = this;
        _panel = (StandingsPanel)Scaler.ScalableContent!;
    }

    public void UpdateRows(IReadOnlyList<object> rows) => _panel.SetRows(rows);

    public void SetSof(double sof) => _panel.SetSof(sof);

    public void SetColumnVisibility(StandingsColumnVisibility visibility) => _panel.ColumnVisibility = visibility;
}
