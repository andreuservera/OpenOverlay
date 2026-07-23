using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App.Dashboard;

/// <summary>
/// Fullscreen, fixed layout meant for a dedicated second monitor: a track map spans the *entire*
/// screen width at the very top, a track info bar spans the top of the center column below that,
/// tire info sits in the top-right corner, flag indicators enlarged in the bottom-left corner,
/// Standings/Relative in the middle, delta + the cockpit cluster (speed/gear/RPM/ABS/proximity, all
/// in one bezel — the same CockpitPanel the floating widget uses) lower-center, closer to eye level
/// since a second monitor is typically mounted above the main one. Not click-through/movable —
/// that's what the floating widgets are for.
/// </summary>
public partial class DashboardWindow : Window
{
    private readonly FuelPanel _fuelPanel;
    private readonly IncidentPanel _incidentPanel;
    private readonly TireInfoPanel _tireInfoPanel;
    private readonly StandingsPanel _standingsPanel;
    private readonly RelativePanel _relativePanel;
    private readonly DeltaPanel _deltaPanel;
    private readonly CockpitPanel _cockpitPanel;
    private readonly FlagPanel _flagPanel;
    private readonly PedalTracePanel _pedalTracePanel;
    private readonly TrackInfoPanel _trackInfoPanel;
    private readonly TrackMapPanel _trackMapPanel;

    public DashboardWindow()
    {
        InitializeComponent();

        _trackMapPanel = (TrackMapPanel)TrackMapScaler.ScalableContent!;
        _trackInfoPanel = (TrackInfoPanel)TrackInfoScaler.ScalableContent!;
        _fuelPanel = (FuelPanel)FuelScaler.ScalableContent!;
        _incidentPanel = (IncidentPanel)IncidentScaler.ScalableContent!;
        _tireInfoPanel = (TireInfoPanel)TireInfoScaler.ScalableContent!;
        _standingsPanel = (StandingsPanel)StandingsScaler.ScalableContent!;
        _relativePanel = (RelativePanel)RelativeScaler.ScalableContent!;
        _deltaPanel = (DeltaPanel)DeltaScaler.ScalableContent!;
        _flagPanel = (FlagPanel)FlagScaler.ScalableContent!;
        _pedalTracePanel = (PedalTracePanel)PedalTraceScaler.ScalableContent!;

        // Structural lookup, not x:Name — naming elements nested inside a ScalablePanel's
        // ContentProperty subtree hits WPF's MC3093 "already had a name registered" error.
        var cockpitViewbox = (Viewbox)CockpitScaler.ScalableContent!;
        _cockpitPanel = (CockpitPanel)cockpitViewbox.Child;

        ApplyTheme(DashboardThemeStore.Get());
    }

    /// <summary>Merges the selected theme's resource dictionary into this window's own Resources —
    /// DynamicResource lookups from anywhere in the Dashboard's tree find these before falling
    /// through to the Classic defaults in App.xaml, while floating widgets (which never merge a
    /// theme dictionary of their own) are unaffected regardless of what's picked here.</summary>
    public void ApplyTheme(DashboardTheme theme)
    {
        Resources.MergedDictionaries.Clear();

        var themeFile = theme switch
        {
            DashboardTheme.DigitalHud => "Themes/DigitalTheme.xaml",
            DashboardTheme.RawDiy => "Themes/RawDiyTheme.xaml",
            _ => null, // Classic: no override, falls through to Application-level defaults
        };

        if (themeFile is not null)
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(themeFile, UriKind.Relative) });
        }
    }

    // Deliberately never uses WindowState.Maximized: with WindowStyle="None" the maximize
    // transition is only reliable the very first time the HWND is shown. On a second Show() after
    // Hide() (the HWND survives Hide(), still carrying WS_MAXIMIZE from the first run) re-applying
    // Maximized is a no-op as far as Windows is concerned, so the window reappears at whatever
    // Normal-state bounds were last set instead of covering the screen. Setting Left/Top/Width/
    // Height directly to the target monitor's full bounds sidesteps the OS maximize state machine
    // entirely and is exactly as "fullscreen" for a chromeless window either way.
    public void MoveToScreen(Screen screen)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;

        if (!IsVisible)
        {
            Show();
        }
    }

    public void UpdateStandingsRows(IReadOnlyList<object> standings) => _standingsPanel.SetRows(standings);

    public void UpdateStandingsSof(double sof) => _standingsPanel.SetSof(sof);

    public void UpdateRelativeRows(IReadOnlyList<RelativeRow> relative) => _relativePanel.SetRows(relative);

    public void UpdateCockpit(CockpitState state) => _cockpitPanel.UpdateState(state);

    // Dashboard has no edit/drag mode of its own — unlike the floating FlagWidget, it always shows
    // an explicit "all clear" placeholder rather than going blank when nothing's happening.
    public void UpdateFlag(IReadOnlyList<FlagState> flags) =>
        _flagPanel.UpdateState(flags.Count > 0 ? flags : [FlagState.None]);

    public void UpdateTireInfo(TireInfoState state) => _tireInfoPanel.UpdateState(state);

    public void UpdateDelta(DeltaState state) => _deltaPanel.UpdateState(state);

    public void UpdateFuel(FuelState state) => _fuelPanel.UpdateState(state);

    public void UpdatePedalTrace(PedalTraceState state) => _pedalTracePanel.UpdateState(state);

    public void UpdateIncident(IncidentState state) => _incidentPanel.UpdateState(state);

    public void UpdateTrackInfo(TrackInfoState state) => _trackInfoPanel.UpdateState(state);

    public void UpdateTrackMap(IReadOnlyList<TrackMapMarker> markers) => _trackMapPanel.UpdateState(markers);
}
