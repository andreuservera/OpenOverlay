using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class IncidentPanel : UserControl
{
    private static readonly Brush Low = StatePalette.TextPrimary;
    private static readonly Brush High = StatePalette.Critical;

    public IncidentPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(IncidentState state)
    {
        MyCountText.Text = state.MyIncidentCount.ToString();
        MyCountText.Foreground = state.MyIncidentCount >= 4 ? High : Low;
        TeamCountText.Text = state.TeamIncidentCount is { } team ? $"team {team}" : "";
    }
}
