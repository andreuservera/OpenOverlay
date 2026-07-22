using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class IncidentPanel : UserControl
{
    private static readonly Brush Low = Brushes.White;
    private static readonly Brush High = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

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
