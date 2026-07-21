using System.Windows;
using IRacingOverlay.App.ViewModels;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App.Dashboard;

/// <summary>
/// Fullscreen, fixed layout meant for a dedicated second monitor: Standings on the left,
/// Relative on the right. Not click-through/movable — that's what the floating widgets are for.
/// </summary>
public partial class DashboardWindow : Window
{
    public DashboardWindow()
    {
        InitializeComponent();
    }

    public void MoveToScreen(Screen screen)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        Left = screen.Bounds.Left + 1;
        Top = screen.Bounds.Top + 1;
        Width = 800;
        Height = 600;

        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Maximized;
    }

    public void UpdateRows(IReadOnlyList<StandingsRow> standings, IReadOnlyList<RelativeRow> relative)
    {
        Standings.SetRows(standings);
        Relative.SetRows(relative);
    }
}
