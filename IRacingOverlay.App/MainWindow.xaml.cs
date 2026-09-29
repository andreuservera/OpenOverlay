using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Dashboard;
using IRacingOverlay.App.Overlay;
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
    // Frame-synchronised rather than a DispatcherTimer: at 16 ms a DispatcherTimer lands on the
    // Windows timer grid (15.6/31.2 ms) at background priority, so the pedal trace sampled unevenly
    // and visibly stuttered. CompositionTarget.Rendering fires once per displayed frame.
    private readonly System.Diagnostics.Stopwatch _criticalClock = System.Diagnostics.Stopwatch.StartNew();
    private double _criticalIntervalMs = 100;
    private double _nextCriticalMs;
    private GlobalHotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private WindowState _restoreState = WindowState.Normal;

    // Set only by a real exit (tray menu, Exit close behavior, Windows shutdown), so the X button
    // can otherwise send the window to the tray.
    private bool _exiting;

    private void ToggleControlPanel()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            HideToTray();
            return;
        }

        RestoreFromTray();
    }

    private void HideToTray() => Hide();

    /// <summary>Back to the state it was in before it went away, and in front of whatever has focus.</summary>
    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = _restoreState;
        }

        // Windows refuses to hand focus to a background app outright; a topmost flip gets it in front.
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting || TrayPreferencesStore.CloseBehavior == CloseBehavior.Exit)
        {
            _exiting = true;
            return;
        }

        e.Cancel = true;
        HideToTray();
        if (!TrayPreferencesStore.TrayNoticeShown)
        {
            _tray?.ShowNotice(
                "OpenOverlay is still running",
                "OpenOverlay will continue running in the background. You can access it from the system tray icon.");
            TrayPreferencesStore.MarkTrayNoticeShown();
        }
    }

    private void UpdateTray()
    {
        if (_tray is null)
        {
            return;
        }

        var (status, text) = _connection.LastError is { } error
            ? (TrayStatus.Error, $"Telemetry error: {error}")
            : !_connection.IsConnected
                ? (TrayStatus.NoSession, "Waiting for iRacing")
                : _vm.OverlaysHidden
                    ? (TrayStatus.OverlaysHidden, "Connected · overlays hidden")
                    : (TrayStatus.Running, "Connected · overlays on");
        _tray.Update(status, $"OpenOverlay — {text}", _vm.OverlaysHidden);
    }
    private readonly PedalTraceBuilder _pedalTraceBuilder = new();
    private readonly FuelBuilder _fuelBuilder = new();
    private readonly FuelCalculatorBuilder _fuelCalculatorBuilder = new();
    private readonly SessionBestLapTracker _sessionBestLapTracker = new();
    private readonly PitStopTracker _pitStopTracker = new();
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

    private readonly FlagPresenter _flagPresenter = new();

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
        Preview.Bind(_vm.StandingsOptions, _vm.RelativeOptions, _vm.FuelCalculatorOptions, _vm.FlagOptions, _vm.FlagPreview, _vm.CockpitOptions, _vm.WeatherOptions);
        DataContext = _vm;

        _criticalIntervalMs = _vm.CriticalRefreshIntervalMs;
        _vm.CriticalRefreshChanged += intervalMs => _criticalIntervalMs = intervalMs;
        _vm.DashboardThemeChanged += theme => _dashboard?.ApplyTheme(theme);
        _vm.DashboardToggleRequested += ToggleDashboard;
        _vm.TableHeaderChanged += PushTableHeader;

        _connection.Connected += (_, _) => Dispatcher.BeginInvoke(() => _vm.IsConnected = true);
        _connection.Disconnected += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _vm.IsConnected = false;
            ClearWidgets();
        });

        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        CompositionTarget.Rendering += OnFrame;

        // Registered against this window's handle, which stays alive while the window is hidden.
        SourceInitialized += (_, _) =>
        {
            _hotkeys = new GlobalHotkeyManager(this);
            _hotkeys.Pressed += _vm.Execute;
            _vm.ReportHotkeyFailures(_hotkeys.Apply(_vm.Hotkeys));
        };
        _vm.HotkeysChanged += () =>
        {
            if (_hotkeys is not null)
            {
                _vm.ReportHotkeyFailures(_hotkeys.Apply(_vm.Hotkeys));
            }
        };
        _vm.HotkeyRecordingChanged += recording =>
        {
            if (recording)
            {
                _hotkeys?.Suspend();
            }
            else if (_hotkeys is not null)
            {
                _vm.ReportHotkeyFailures(_hotkeys.Resume());
            }
        };
        _vm.ControlPanelToggleRequested += ToggleControlPanel;

        _tray = new TrayIcon();
        _tray.OpenRequested += RestoreFromTray;
        _tray.ToggleOverlaysRequested += () => _vm.OverlaysHidden = !_vm.OverlaysHidden;
        _tray.RestartOverlaysRequested += _vm.RestartOverlays;
        _tray.OpenConfigFolderRequested += ControlPanelViewModel.OpenConfigFolder;
        _tray.ExitRequested += ExitApplication;
        UpdateTray();

        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                _restoreState = WindowState;
            }
        };
        Closing += OnClosing;
        // Signing out or shutting down must never be held up by the close-to-tray behavior.
        System.Windows.Application.Current.SessionEnding += (_, _) => _exiting = true;

        Closed += (_, _) =>
        {
            _uiTimer.Stop();
            CompositionTarget.Rendering -= OnFrame;
            _hotkeys?.Dispose();
            _tray?.Dispose();
            _tray = null;
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
    private WeatherWidget? Weather => _vm.WidgetOf<WeatherWidget>(WidgetCatalog.Weather);
    private TrackMapWidget? TrackMap => _vm.WidgetOf<TrackMapWidget>(WidgetCatalog.TrackMap);
    private FuelCalculatorWidget? FuelCalculator => _vm.WidgetOf<FuelCalculatorWidget>(WidgetCatalog.FuelCalculator);

    /// <summary>
    /// Puts every widget and the dashboard back to its no-data state when iRacing goes away. The
    /// update loop stops pushing once there's no telemetry, so without this the last session's
    /// numbers would stay on screen looking live. The tables show "Waiting for iRacing telemetry…".
    /// </summary>
    private void ClearWidgets()
    {
        _latestStandings = [];
        _vm.TelemetryLine = "Waiting for iRacing";

        Relative?.UpdateRows([]);
        Standings?.UpdateRows([]);
        Standings?.SetSof(0);
        Cockpit?.UpdateState(CockpitState.Empty);
        Flags?.UpdateState([FlagState.None]);
        Tires?.UpdateState(TireInfoState.Empty);
        Delta?.UpdateState(DeltaState.Empty);
        Fuel?.UpdateState(FuelState.Empty);
        Pedals?.UpdateState(PedalTraceState.Empty);
        Incidents?.UpdateState(IncidentState.Empty);
        TrackInfo?.UpdateState(TrackInfoState.Empty);
        Weather?.UpdateState(WeatherState.Empty);
        TrackMap?.UpdateState([]);
        FuelCalculator?.UpdateState(FuelCalculatorState.Empty);

        if (_dashboard is { } dashboard)
        {
            dashboard.UpdateStandingsRows([]);
            dashboard.UpdateStandingsSof(0);
            dashboard.UpdateRelativeRows([]);
            dashboard.UpdateCockpit(CockpitState.Empty);
            dashboard.UpdateFlag([FlagState.None]);
            dashboard.UpdateTireInfo(TireInfoState.Empty);
            dashboard.UpdateDelta(DeltaState.Empty);
            dashboard.UpdateFuel(FuelState.Empty);
            dashboard.UpdatePedalTrace(PedalTraceState.Empty);
            dashboard.UpdateIncident(IncidentState.Empty);
            dashboard.UpdateTrackInfo(TrackInfoState.Empty);
            dashboard.UpdateTrackMap([]);
        }
    }

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
            UpdateTray();
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

        Units.Observe(telemetry);
        UpdateTelemetryLine(telemetry);

        var session = _connection.Session;
        _tickCount++;
        // Every tick, whatever is open: a stop is timed on entry and exit, and a missed edge loses it.
        _pitStopTracker.Update(telemetry, session);
        // Relative needs the standings order too, for its POS and iRΔ columns, so this runs
        // whenever any of the three consumers is open — not just the two that display it directly.
        var needsStandings = Standings is not null || _dashboard is not null || Relative is not null;
        if (needsStandings && _tickCount % StandingsUpdateEveryNTicks == 0)
        {
            _latestStandings = StandingsBuilder.BuildStandings(telemetry, session, _sessionBestLapTracker, _pitStopTracker.LastStops);

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
        var relativeRows = StandingsBuilder.BuildRelative(
            telemetry, session, _vm.RelativeOptions.FocusSize, _latestStandings, _pitStopTracker.LastStops);
        if (Relative is { } relative)
        {
            relative.UpdateRows(relativeRows);
            relative.SetCarName(StandingsBuilder.SingleClassCarName(session));
            relative.SetSessionId(session?.WeekendInfo?.SubSessionID ?? 0);
        }

        _dashboard?.UpdateRelativeRows(relativeRows);

        if (Flags is not null || _dashboard is not null)
        {
            var flagStates = _flagPresenter.Present(
                FlagBuilder.Decode(telemetry), _vm.FlagOptions, TimeSpan.FromMilliseconds(Environment.TickCount64));
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
            var incidentState = IncidentBuilder.Build(telemetry, session);
            Incidents?.UpdateState(incidentState);
            _dashboard?.UpdateIncident(incidentState);
        }

        if (TrackInfo is not null || _dashboard is not null)
        {
            var trackInfoState = TrackInfoBuilder.Build(telemetry, session);
            TrackInfo?.UpdateState(trackInfoState);
            _dashboard?.UpdateTrackInfo(trackInfoState);
        }

        // Every tick: the wind arrow follows the car's heading, which changes through every corner.
        Weather?.UpdateState(WeatherBuilder.Build(telemetry, session));

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

    private void OnFrame(object? sender, EventArgs e)
    {
        // A few ms of slack, so a 16 ms target runs on every 60 Hz frame (16.7 ms apart).
        var now = _criticalClock.Elapsed.TotalMilliseconds;
        if (now < _nextCriticalMs - 3)
        {
            return;
        }

        _nextCriticalMs = now + _criticalIntervalMs;
        CriticalTimer_Tick(sender, e);
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
            var pedalTraceState = _pedalTraceBuilder.Build(telemetry);
            Pedals?.UpdateState(pedalTraceState);
            _dashboard?.UpdatePedalTrace(pedalTraceState);
        }
    }

    private void UpdateTelemetryLine(TelemetrySnapshot telemetry)
    {
        var units = Units.Read(telemetry);
        var speed = telemetry.HasVariable("Speed") ? Units.SpeedFromMs(telemetry.GetFloat("Speed"), units) : 0;
        var lap = telemetry.HasVariable("Lap") ? telemetry.GetInt("Lap") : 0;
        var gear = telemetry.HasVariable("Gear") ? telemetry.GetInt("Gear") : 0;
        _vm.TelemetryLine = $"Speed {speed:0} {Units.SpeedUnit(units)}    Lap {lap}    Gear {gear}";
    }

    private void UpdateDiagnostics()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var megabytes = process.WorkingSet64 / (1024.0 * 1024.0);
        var uiAvg = _uiTickSamples > 0 ? _uiTickTotalMs / _uiTickSamples : 0;
        var criticalAvg = _criticalTickSamples > 0 ? _criticalTickTotalMs / _criticalTickSamples : 0;
        var criticalTargetMs = _criticalIntervalMs;

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
        dashboard.SetFlagOptions(_vm.FlagOptions);
        dashboard.SetCockpitOptions(_vm.CockpitOptions);
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
