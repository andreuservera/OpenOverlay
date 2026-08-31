using System.Windows;
using System.Windows.Threading;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Dashboard;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;
using IRacingOverlay.Sdk;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App;

/// <summary>
/// The control panel window. Two jobs, kept apart on purpose:
///
///   • it hosts <see cref="ControlPanelViewModel"/>, which owns everything the user configures, and
///   • it runs the telemetry loops that feed the widgets.
///
/// Nothing in this file reads a control back out any more. Every setting lives in the view model,
/// every widget's lifecycle lives in its <c>WidgetSlot</c>, and the loops below reach widgets
/// through those slots — so adding a widget adds nothing here except the line that pushes its state.
/// </summary>
public partial class MainWindow : Window
{
    // Standings only needs to feel "live," not sub-second precise — recomputing every 100ms was
    // wasted work (and, before the continuous-ordering fix, it happened to disguise a bug: since the
    // underlying data barely changed within a lap either way, it *looked* like updates only landed
    // at lap boundaries). This throttles it to roughly once a second without a second timer.
    private const int StandingsUpdateEveryNTicks = 10;

    private readonly ControlPanelViewModel _vm;

    private readonly IRacingConnection _connection = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    // Cockpit (proximity/ABS bars) and the pedal trace are the two displays where update rate is
    // itself the whole point — they're read on their own timer, decoupled from the general 100ms
    // tick, so the user can push them faster (lower latency, more CPU) or slower independently of
    // everything else.
    private readonly DispatcherTimer _criticalTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly PedalTraceBuilder _pedalTraceBuilder = new();
    private readonly FuelBuilder _fuelBuilder = new();
    private readonly FuelCalculatorBuilder _fuelCalculatorBuilder = new();
    private readonly SessionBestLapTracker _sessionBestLapTracker = new();
    private int _tickCount;

    // Perf diagnostics for the reported "stutter, even at low Hz" — both timers share one UI thread
    // with WPF's own layout/render passes, so a slow tick BODY (data-processing time) and a late tick
    // FIRING (the Dispatcher not getting around to it on schedule — e.g. because the other timer's
    // tick, or a GC pause, or the OS scheduler, is hogging that same thread) are two different
    // possible causes and need to be told apart. Reset once a second in UpdateDiagnostics.
    private readonly System.Diagnostics.Stopwatch _uiTickStopwatch = new();
    private double _uiTickMaxMs;
    private double _uiTickTotalMs;
    private int _uiTickSamples;

    private readonly System.Diagnostics.Stopwatch _criticalTickStopwatch = new();
    private double _criticalTickMaxMs;
    private double _criticalTickTotalMs;
    private int _criticalTickSamples;
    private long _lastCriticalTickTimestampMs = -1;
    private double _criticalTickMaxGapMs;

    private DashboardWindow? _dashboard;

    // Last computed running order, shared with Relative so both tables report the same position for
    // the same driver. Rebuilt on the standings tick, not every frame.
    private IReadOnlyList<StandingsRow> _latestStandings = [];

    public MainWindow()
    {
        // Built before InitializeComponent so every persisted value is already loaded when the first
        // binding evaluates. The old panel had the opposite problem: XAML-declared defaults on the
        // ComboBoxes fired their SelectionChanged handlers during construction and wrote those
        // defaults straight over the user's saved settings. There are no XAML-declared values left
        // to do that — every control's value comes from the view model, which read the stores first.
        _vm = new ControlPanelViewModel();

        InitializeComponent();

        // Before DataContext, so the preview already knows which options objects to follow by the
        // time the Slot binding hands it its first widget.
        Preview.Bind(_vm.StandingsOptions, _vm.RelativeOptions, _vm.FuelCalculatorOptions);
        DataContext = _vm;

        _criticalTimer.Interval = TimeSpan.FromMilliseconds(_vm.CriticalRefreshIntervalMs);
        _vm.CriticalRefreshChanged += intervalMs => _criticalTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        _vm.DashboardThemeChanged += theme => _dashboard?.ApplyTheme(theme);
        _vm.DashboardToggleRequested += ToggleDashboard;
        _vm.TableHeaderChanged += PushTableHeader;

        _connection.Connected += (_, _) => Dispatcher.BeginInvoke(() => _vm.IsConnected = true);
        _connection.Disconnected += (_, _) => Dispatcher.BeginInvoke(() => _vm.IsConnected = false);

        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        _criticalTimer.Tick += CriticalTimer_Tick;
        _criticalTimer.Start();

        Closed += (_, _) =>
        {
            _uiTimer.Stop();
            _criticalTimer.Stop();
            _connection.Stop();
            _vm.CloseAllWidgets();
            _dashboard?.Close();
        };

        // Re-opens whichever widgets were on screen last run, in the layout mode currently selected.
        _vm.RestoreVisibleWidgets();

        _connection.Start();
    }

    // Typed handles for the loops below. Each is a dictionary lookup and a cast against a slot that
    // may not have created its window yet, so "widget is off" reads as null everywhere rather than
    // as a separate flag to keep in step.
    private RelativeWidget? Relative => _vm.WidgetOf<RelativeWidget>(WidgetCatalog.Relative);
    private StandingsWidget? Standings => _vm.WidgetOf<StandingsWidget>(WidgetCatalog.Standings);
    private CockpitWidget? Cockpit => _vm.WidgetOf<CockpitWidget>(WidgetCatalog.Cockpit);
    private FlagWidget? Flags => _vm.WidgetOf<FlagWidget>(WidgetCatalog.Flag);
    private TireInfoWidget? Tires => _vm.WidgetOf<TireInfoWidget>(WidgetCatalog.TireInfo);
    private DeltaWidget? Delta => _vm.WidgetOf<DeltaWidget>(WidgetCatalog.Delta);
    private FuelWidget? Fuel => _vm.WidgetOf<FuelWidget>(WidgetCatalog.Fuel);
    private PedalTraceWidget? Pedals => _vm.WidgetOf<PedalTraceWidget>(WidgetCatalog.PedalTrace);
    private IncidentWidget? Incidents => _vm.WidgetOf<IncidentWidget>(WidgetCatalog.Incident);
    private TrackInfoWidget? TrackInfo => _vm.WidgetOf<TrackInfoWidget>(WidgetCatalog.TrackInfo);
    private TrackMapWidget? TrackMap => _vm.WidgetOf<TrackMapWidget>(WidgetCatalog.TrackMap);
    private FuelCalculatorWidget? FuelCalculator => _vm.WidgetOf<FuelCalculatorWidget>(WidgetCatalog.FuelCalculator);

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        _uiTickStopwatch.Restart();
        try
        {
            UiTimer_TickCore();
        }
        finally
        {
            RecordTickDuration(_uiTickStopwatch.Elapsed.TotalMilliseconds, ref _uiTickTotalMs, ref _uiTickMaxMs, ref _uiTickSamples);
        }
    }

    private void UiTimer_TickCore()
    {
        var telemetry = _connection.Latest;

        // Evaluated before the no-telemetry bail-out, and from the connection as well as the
        // snapshot. Both matter: this used to sit after the bail-out, so with iRacing closed the
        // loop returned early and every widget stayed on screen forever — and IRacingConnection
        // keeps the last snapshot it read after a disconnect, so the stale IsOnTrack in it would
        // have answered "still driving" even once the sim was gone.
        ApplyAutoHideVisibility(IsPlayerDriving(telemetry));

        if (telemetry is null)
        {
            return;
        }

        UpdateTelemetryLine(telemetry);

        var session = _connection.Session;
        _tickCount++;
        // Relative needs the standings order too, for its POS and iRΔ columns, so this runs
        // whenever any of the three consumers is open — not just the two that display it directly.
        var needsStandings = Standings is not null || _dashboard is not null || Relative is not null;
        if (needsStandings && _tickCount % StandingsUpdateEveryNTicks == 0)
        {
            _latestStandings = StandingsBuilder.BuildStandings(telemetry, session, _sessionBestLapTracker);

            // Only the two widgets that display SOF pay for it — Relative pulls the running order
            // out of this block but has no use for the field strength.
            var sof = Standings is not null || _dashboard is not null
                ? StandingsBuilder.ComputeStrengthOfField(session)
                : 0;
            if (Standings is { } standings)
            {
                // The floating widget gets the compact focused view (podium + a block around the
                // player); the Dashboard has the room for the whole field, grouped by class.
                standings.UpdateRows(_vm.StandingsOptions.ShowMulticlass
                    ? StandingsBuilder.BuildMulticlassView(_latestStandings, _vm.StandingsOptions.FocusSize)
                    : StandingsBuilder.BuildFocusedView(_latestStandings, _vm.StandingsOptions.FocusSize));
                standings.SetSof(sof);
                standings.SetCarName(StandingsBuilder.SingleClassCarName(session));
                standings.SetSessionId(session?.WeekendInfo?.SubSessionID ?? 0);
            }

            if (_dashboard is not null)
            {
                var grouped = StandingsBuilder.GroupForDisplay(_latestStandings);
                _dashboard.UpdateStandingsRows(grouped);
                _dashboard.UpdateStandingsSof(sof);
                _dashboard.UpdateStandingsCarName(StandingsBuilder.SingleClassCarName(session));
            }
        }

        // Built every tick, unlike standings: Relative is about where cars are right now, and a
        // once-a-second refresh is visibly laggy when someone is alongside you.
        var relativeRows = StandingsBuilder.BuildRelative(telemetry, session, _vm.RelativeOptions.FocusSize, _latestStandings);
        if (Relative is { } relative)
        {
            relative.UpdateRows(relativeRows);
            relative.SetCarName(StandingsBuilder.SingleClassCarName(session));
            relative.SetSessionId(session?.WeekendInfo?.SubSessionID ?? 0);
        }

        _dashboard?.UpdateRelativeRows(relativeRows);

        if (Flags is not null || _dashboard is not null)
        {
            var flagStates = FlagBuilder.Build(telemetry);
            Flags?.UpdateState(flagStates);
            _dashboard?.UpdateFlag(flagStates);
        }

        if (Tires is not null || _dashboard is not null)
        {
            var tireInfoState = TireInfoBuilder.Build(telemetry);
            Tires?.UpdateState(tireInfoState);
            _dashboard?.UpdateTireInfo(tireInfoState);
        }

        if (Delta is not null || _dashboard is not null)
        {
            var deltaState = DeltaBuilder.Build(telemetry, _vm.DeltaReference);
            Delta?.UpdateState(deltaState);
            _dashboard?.UpdateDelta(deltaState);
        }

        if (Fuel is not null || _dashboard is not null)
        {
            var fuelState = _fuelBuilder.Build(telemetry);
            Fuel?.UpdateState(fuelState);
            _dashboard?.UpdateFuel(fuelState);
        }

        if (FuelCalculator is { } fuelCalculator)
        {
            fuelCalculator.UpdateState(_fuelCalculatorBuilder.Build(telemetry, session, _vm.FuelCalculatorOptions));
        }

        if (Incidents is not null || _dashboard is not null)
        {
            var incidentState = IncidentBuilder.Build(telemetry);
            Incidents?.UpdateState(incidentState);
            _dashboard?.UpdateIncident(incidentState);
        }

        if (TrackInfo is not null || _dashboard is not null)
        {
            var trackInfoState = TrackInfoBuilder.Build(telemetry, session);
            TrackInfo?.UpdateState(trackInfoState);
            _dashboard?.UpdateTrackInfo(trackInfoState);
        }

        if (TrackMap is not null || _dashboard is not null)
        {
            var trackMapMarkers = TrackMapBuilder.Build(telemetry, session);
            TrackMap?.UpdateState(trackMapMarkers);
            _dashboard?.UpdateTrackMap(trackMapMarkers);
        }

        // Memory usage barely changes tick to tick — reuse the same once-a-second cadence as
        // Standings rather than recomputing it on every 100ms tick.
        if (_tickCount % StandingsUpdateEveryNTicks == 0)
        {
            UpdateDiagnostics();
        }
    }

    /// <summary>
    /// Whether the player is actually at the wheel right now — the single question "hide when I'm
    /// not driving" turns on.
    ///
    /// Three conditions, and all three are load-bearing. No live connection means iRacing is closed
    /// or has been exited, and the snapshot still held from before it went away must not be trusted.
    /// No snapshot at all means nothing has been read yet. And IsOnTrack, per iRacing's own SDK
    /// docs, is true "only when the player is running the physics for the car and is currently in
    /// the car" — false at the main menu, in the garage, on a setup screen, while spectating and
    /// during replays, which is the rest of what the option promises.
    ///
    /// A car that simply doesn't publish IsOnTrack falls back to "driving": failing open leaves a
    /// widget visible when it could have hidden, while failing closed would blank someone's overlay
    /// mid-race over a missing variable.
    /// </summary>
    private bool IsPlayerDriving(TelemetrySnapshot? telemetry)
    {
        if (!_connection.IsConnected || telemetry is null)
        {
            return false;
        }

        return !telemetry.HasVariable(TelemetryVarNames.IsOnTrack) || telemetry.GetBool(TelemetryVarNames.IsOnTrack);
    }

    /// <summary>
    /// Hides (or reveals) every widget whose "hide outside car" option is on, without touching the
    /// enabled state itself — so a widget picks back up exactly where it was the moment the player
    /// gets back in the car.
    ///
    /// The decision lives in the slot, not here: showing a widget and then hiding it from a
    /// different code path a tick later is what used to make one flash on screen at startup.
    /// </summary>
    private void ApplyAutoHideVisibility(bool isDriving)
    {
        foreach (var slot in _vm.Slots)
        {
            slot.IsDriving = isDriving;
        }
    }

    private void CriticalTimer_Tick(object? sender, EventArgs e)
    {
        var nowMs = Environment.TickCount64;
        if (_lastCriticalTickTimestampMs >= 0)
        {
            var gap = nowMs - _lastCriticalTickTimestampMs;
            if (gap > _criticalTickMaxGapMs)
            {
                _criticalTickMaxGapMs = gap;
            }
        }

        _lastCriticalTickTimestampMs = nowMs;

        _criticalTickStopwatch.Restart();
        try
        {
            CriticalTimer_TickCore();
        }
        finally
        {
            RecordTickDuration(_criticalTickStopwatch.Elapsed.TotalMilliseconds, ref _criticalTickTotalMs, ref _criticalTickMaxMs, ref _criticalTickSamples);
        }
    }

    private void CriticalTimer_TickCore()
    {
        var telemetry = _connection.Latest;
        if (telemetry is null)
        {
            return;
        }

        if (Cockpit is not null || _dashboard is not null)
        {
            var cockpitState = CockpitBuilder.Build(telemetry, _connection.Session);
            Cockpit?.UpdateState(cockpitState);
            _dashboard?.UpdateCockpit(cockpitState);
        }

        if (Pedals is not null || _dashboard is not null)
        {
            var pedalTraceState = _pedalTraceBuilder.Build(telemetry, _criticalTimer.Interval.TotalMilliseconds);
            Pedals?.UpdateState(pedalTraceState);
            _dashboard?.UpdatePedalTrace(pedalTraceState);
        }
    }

    private void UpdateTelemetryLine(TelemetrySnapshot telemetry)
    {
        var speed = telemetry.HasVariable("Speed") ? telemetry.GetFloat("Speed") * 3.6 : 0; // m/s -> km/h
        var lap = telemetry.HasVariable("Lap") ? telemetry.GetInt("Lap") : 0;
        var gear = telemetry.HasVariable("Gear") ? telemetry.GetInt("Gear") : 0;
        _vm.TelemetryLine = $"Speed {speed:0} km/h    Lap {lap}    Gear {gear}";
    }

    private void UpdateDiagnostics()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var megabytes = process.WorkingSet64 / (1024.0 * 1024.0);
        var uiAvg = _uiTickSamples > 0 ? _uiTickTotalMs / _uiTickSamples : 0;
        var criticalAvg = _criticalTickSamples > 0 ? _criticalTickTotalMs / _criticalTickSamples : 0;
        var criticalTargetMs = _criticalTimer.Interval.TotalMilliseconds;

        _vm.DiagnosticsLine =
            $"{megabytes:0} MB · GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} · " +
            $"UI {uiAvg:0.0}/{_uiTickMaxMs:0.0} ms · " +
            $"critical {criticalAvg:0.0}/{_criticalTickMaxMs:0.0} ms (target {criticalTargetMs:0}, worst gap {_criticalTickMaxGapMs:0})";

        // Rolling ~1s window (this is called once every StandingsUpdateEveryNTicks UI ticks) rather
        // than a since-launch average — a stutter from 10 minutes ago shouldn't still be dragging
        // down what the user sees right now.
        _uiTickTotalMs = 0;
        _uiTickMaxMs = 0;
        _uiTickSamples = 0;
        _criticalTickTotalMs = 0;
        _criticalTickMaxMs = 0;
        _criticalTickSamples = 0;
        _criticalTickMaxGapMs = 0;
    }

    private static void RecordTickDuration(double elapsedMs, ref double totalMs, ref double maxMs, ref int samples)
    {
        totalMs += elapsedMs;
        samples++;
        if (elapsedMs > maxMs)
        {
            maxMs = elapsedMs;
        }
    }

    /// <summary>The driver tables' header fields are pushed on the standings tick, so a header
    /// switched off has to be cleared now rather than leaving a stale value on screen for up to a
    /// second.</summary>
    private void PushTableHeader(DriverTable table)
    {
        var session = _connection.Session;
        var carName = StandingsBuilder.SingleClassCarName(session);
        var subSessionId = session?.WeekendInfo?.SubSessionID ?? 0;

        if (table == DriverTable.Standings)
        {
            Standings?.SetCarName(carName);
            Standings?.SetSessionId(subSessionId);
        }
        else
        {
            Relative?.SetCarName(carName);
            Relative?.SetSessionId(subSessionId);
        }
    }

    private void ToggleDashboard()
    {
        if (_dashboard is { IsVisible: true })
        {
            _dashboard.Hide();
            SetDashboardButtonCaption("Show dashboard");
            return;
        }

        var screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            return;
        }

        _dashboard ??= CreateDashboard();
        _dashboard.MoveToScreen(screens[Math.Clamp(_vm.SelectedMonitorIndex, 0, screens.Length - 1)]);
        SetDashboardButtonCaption("Hide dashboard");
    }

    private DashboardWindow CreateDashboard()
    {
        var dashboard = new DashboardWindow();
        dashboard.Closed += (_, _) => SetDashboardButtonCaption("Show dashboard");
        return dashboard;
    }

    private void SetDashboardButtonCaption(string caption)
    {
        if (_vm.DashboardButton is { } button)
        {
            button.ButtonText = caption;
        }
    }
}
