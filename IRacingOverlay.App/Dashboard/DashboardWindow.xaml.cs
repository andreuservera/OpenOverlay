using System.Windows;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App.Dashboard;

/// <summary>
/// Fullscreen, fixed layout meant for a dedicated second monitor: flag + tire info in the top
/// corners, Standings/Relative in the middle, gear + shift lights lower-center (closer to eye level,
/// since a second monitor is typically mounted above the main one), and the ABS/TC/proximity bars
/// running the full height of the left and right edges. Not click-through/movable — that's what the
/// floating widgets are for.
/// </summary>
public partial class DashboardWindow : Window
{
    private static readonly Color AbsIdle = Color.FromRgb(0x2E, 0x1E, 0x1E);
    private static readonly Color AbsDim = Color.FromRgb(0x80, 0x18, 0x18);
    private static readonly Color AbsBright = Color.FromRgb(0xFF, 0x30, 0x30);

    private static readonly Color TcIdle = Color.FromRgb(0x22, 0x1E, 0x2E);
    private static readonly Color TcDim = Color.FromRgb(0x60, 0x18, 0x80);
    private static readonly Color TcBright = Color.FromRgb(0xB0, 0x30, 0xFF);

    private bool _blinkPhase;

    public DashboardWindow()
    {
        InitializeComponent();
        AbsBar.Configure(AbsIdle, AbsDim, AbsBright);
        TcBar.Configure(TcIdle, TcDim, TcBright);
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

    public void UpdateStandingsRows(IReadOnlyList<StandingsRow> standings) => Standings.SetRows(standings);

    public void UpdateRelativeRows(IReadOnlyList<RelativeRow> relative) => Relative.SetRows(relative);

    public void UpdateCockpit(CockpitState state)
    {
        _blinkPhase = !_blinkPhase;

        ShiftGear.UpdateState(state.Gear, state.ShiftLightsLit, state.ShiftBlink, _blinkPhase);
        AbsBar.SetActive(state.AbsActive, _blinkPhase);
        TcBar.SetActive(state.TcActive, _blinkPhase);
        LeftProximity.SetFraction(state.LeftProximity);
        RightProximity.SetFraction(state.RightProximity);
    }

    public void UpdateFlag(FlagState state) => Flag.UpdateState(state);

    public void UpdateTireInfo(TireInfoState state) => TireInfo.UpdateState(state);
}
