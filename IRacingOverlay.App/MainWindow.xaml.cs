using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Dashboard;
using IRacingOverlay.App.Diagnostics;
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
    private const int HealthUpdateEveryNTicks = 10;
    private static readonly TimeSpan RecentProblemWindow = TimeSpan.FromMinutes(10);

    private readonly ControlPanelViewModel _vm;

    private readonly IRacingConnection _connection = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    // Every step of both update loops runs inside a guard, so a widget that throws is logged,
    // paused and rebuilt on its own while every other widget keeps updating.
    private readonly HealthMonitor _health = new();
    private readonly Dictionary<string, ComponentGuard> _widgetGuards = [];
    private readonly ComponentGuard _dashboardGuard;
    private readonly ComponentGuard _standingsGuard;
    private readonly ComponentGuard _pitStopGuard;
    private readonly ComponentGuard _statusGuard;
    private readonly ComponentGuard _autoHideGuard;
    private readonly ComponentGuard _healthGuard;
    private readonly ComponentGuard _trayGuard;
    private readonly UiWatchdog _watchdog = new();
    private readonly UpdateService _updates;
    private int _healthTicks;

    // Which event the per-session trackers describe. Kept across a disconnect, so rejoining the same
    // race after an iRacing crash picks up its fuel history, pit stops and best laps again.
    private IracingSessionInfo? _observedSession;
    private string? _sessionKey;
    private string? _sessionDescription;
    private bool _awaitingReconnect;
    private volatile string? _telemetryErrorRef;
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

    private void HideToTray()
    {
        AppLog.Activity("Control Panel", "Hidden to the tray");
        Hide();
    }

    /// <summary>Back to the state it was in before it went away, and in front of whatever has focus.</summary>
    internal void RestoreFromTray()
    {
        AppLog.Activity("Control Panel", "Opened");
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
        AppLog.Activity("Control Panel", "Exit requested");
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

        var (status, text) = _vm.Health == HealthStatus.Failed
            ? (TrayStatus.Error, $"Problem: {_vm.HealthLine}")
            : _connection.LastError is { } error
            ? (TrayStatus.Error, $"Telemetry error: {error}")
            : !_connection.IsConnected
                ? (TrayStatus.NoSession, "Waiting for iRacing")
                : _vm.OverlaysHidden
                    ? (TrayStatus.OverlaysHidden, "Connected · overlays hidden")
                    : (TrayStatus.Running, "Connected · overlays on");
        _tray.Update(status, $"OpenOverlay — {text}", _vm.OverlaysHidden);
    }
    private PedalTraceBuilder _pedalTraceBuilder = new();
    private FuelBuilder _fuelBuilder = new();
    private FuelCalculatorBuilder _fuelCalculatorBuilder = new();
    private SessionBestLapTracker _sessionBestLapTracker = new();
    private PitStopTracker _pitStopTracker = new();
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

    private FlagPresenter _flagPresenter = new();

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

        foreach (var descriptor in WidgetCatalog.All)
        {
            var key = descriptor.Key;
            _widgetGuards[key] = _health.CreateGuard($"Widget: {descriptor.Name}", stage => RecoverWidget(key, stage));
        }

        _dashboardGuard = _health.CreateGuard("Dashboard", _ => RecoverDashboard());
        _standingsGuard = _health.CreateGuard("Standings model", _ => _sessionBestLapTracker = new());
        _pitStopGuard = _health.CreateGuard("Pit stop tracker", _ => _pitStopTracker = new());
        _statusGuard = _health.CreateGuard("Status line");
        _autoHideGuard = _health.CreateGuard("Auto-hide");
        _healthGuard = _health.CreateGuard("Health report");
        _trayGuard = _health.CreateGuard("Tray icon");
        GlobalExceptionHandler.StormRecovery = RecoverOverlays;

        _criticalIntervalMs = _vm.CriticalRefreshIntervalMs;
        _vm.CriticalRefreshChanged += intervalMs => _criticalIntervalMs = intervalMs;
        _vm.DashboardThemeChanged += theme => _dashboardGuard.Run(() => _dashboard?.ApplyTheme(theme));
        _vm.DashboardToggleRequested += ToggleDashboard;
        _vm.TableHeaderChanged += PushTableHeader;

        _connection.Fault += OnConnectionFault;
        _connection.Connected += (_, _) =>
        {
            AppLog.Info("Telemetry", "Connected to iRacing");
            Dispatcher.BeginInvoke(() => _vm.IsConnected = true);
        };
        _connection.Disconnected += (_, _) =>
        {
            AppLog.Info("Telemetry", "Disconnected from iRacing");
            Dispatcher.BeginInvoke(() =>
            {
                _vm.IsConnected = false;
                _observedSession = null;
                _awaitingReconnect = _sessionKey is not null;
                AppLog.Session = null;
                ClearWidgets();
            });
        };

        _updates = new UpdateService(() => _connection.IsConnected);

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
        if (AppInfo.IsRecoveredLaunch)
        {
            _tray.ShowNotice(
                "OpenOverlay restarted itself",
                "It recovered from a problem on its own. A report was saved — Control Panel › General › Diagnostics.");
        }

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
            // The watchdog first: once the loop stops beating, a live watchdog would call it a hang.
            _watchdog.Dispose();
            _uiTimer.Stop();
            CompositionTarget.Rendering -= OnFrame;
            GlobalExceptionHandler.StormRecovery = null;
            _updates.Dispose();
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
        _watchdog.Start();
        _updates.Start();
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

        Clear(WidgetCatalog.Relative, Relative, w => w.UpdateRows([]));
        Clear(WidgetCatalog.Standings, Standings, w =>
        {
            w.UpdateRows([]);
            w.SetSof(0);
        });
        Clear(WidgetCatalog.Cockpit, Cockpit, w => w.UpdateState(CockpitState.Empty));
        Clear(WidgetCatalog.Flag, Flags, w => w.UpdateState([FlagState.None]));
        Clear(WidgetCatalog.TireInfo, Tires, w => w.UpdateState(TireInfoState.Empty));
        Clear(WidgetCatalog.Delta, Delta, w => w.UpdateState(DeltaState.Empty));
        Clear(WidgetCatalog.Fuel, Fuel, w => w.UpdateState(FuelState.Empty));
        Clear(WidgetCatalog.PedalTrace, Pedals, w => w.UpdateState(PedalTraceState.Empty));
        Clear(WidgetCatalog.Incident, Incidents, w => w.UpdateState(IncidentState.Empty));
        Clear(WidgetCatalog.TrackInfo, TrackInfo, w => w.UpdateState(TrackInfoState.Empty));
        Clear(WidgetCatalog.Weather, Weather, w => w.UpdateState(WeatherState.Empty));
        Clear(WidgetCatalog.TrackMap, TrackMap, w => w.UpdateState([]));
        Clear(WidgetCatalog.FuelCalculator, FuelCalculator, w => w.UpdateState(FuelCalculatorState.Empty));

        if (_dashboard is { } dashboard)
        {
            _dashboardGuard.Run(() =>
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
            });
        }
    }

    private void Clear<TWidget>(string key, TWidget? widget, Action<TWidget> clear) where TWidget : OverlayWindowBase
    {
        if (widget is not null)
        {
            _widgetGuards[key].Run(() => clear(widget));
        }
    }

    /// <summary>
    /// Builds one widget's state and hands it to the floating widget and the dashboard, each step
    /// under a guard: a builder that throws leaves the windows showing their previous state, a window
    /// that throws doesn't stop the dashboard getting the same state. Nothing is built for nobody.
    /// </summary>
    private void Feed<TWidget, TState>(
        string key,
        TWidget? widget,
        Func<TState> build,
        Action<TWidget, TState> toWidget,
        Action<DashboardWindow, TState>? toDashboard)
        where TWidget : OverlayWindowBase
    {
        var dashboard = toDashboard is null ? null : _dashboard;
        if (widget is null && dashboard is null)
        {
            return;
        }

        var guard = _widgetGuards[key];
        if (!guard.TryRun(build, out var state))
        {
            return;
        }

        if (widget is not null)
        {
            guard.Run(() => toWidget(widget, state));
        }

        if (dashboard is not null)
        {
            _dashboardGuard.Run(() => toDashboard!(dashboard, state));
        }
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        _watchdog.Beat();
        _uiTickStopwatch.Restart();
        try
        {
            UiTimer_TickCore();
        }
        finally
        {
            RecordTickDuration(_uiTickStopwatch.Elapsed.TotalMilliseconds, ref _uiTickTotalMs, ref _uiTickMaxMs, ref _uiTickSamples);
            if (++_healthTicks % HealthUpdateEveryNTicks == 0)
            {
                _healthGuard.Run(UpdateHealth);
            }

            _trayGuard.Run(UpdateTray);
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
        // A failure here fails open (driving), for the same reason a missing IsOnTrack does below.
        var isDriving = !_autoHideGuard.TryRun(() => IsPlayerDriving(telemetry), out var driving) || driving;
        ApplyAutoHideVisibility(isDriving);

        if (telemetry is null)
        {
            return;
        }

        var session = _connection.Session;
        ObserveSession(telemetry, session);
        _statusGuard.Run(() =>
        {
            Units.Observe(telemetry);
            UpdateTelemetryLine(telemetry);
        }, GuardStage.Build);

        _tickCount++;
        // Every tick, whatever is open: a stop is timed on entry and exit, and a missed edge loses it.
        _pitStopGuard.Run(() => _pitStopTracker.Update(telemetry, session), GuardStage.Build);
        // Relative needs the standings order too, for its POS and iRΔ columns, so this runs
        // whenever any of the three consumers is open — not just the two that display it directly.
        var needsStandings = Standings is not null || _dashboard is not null || Relative is not null;
        if (needsStandings && _tickCount % StandingsUpdateEveryNTicks == 0)
        {
            // A failed rebuild keeps the last good order: a table a second old beats an empty one.
            if (_standingsGuard.TryRun(
                    () => StandingsBuilder.BuildStandings(telemetry, session, _sessionBestLapTracker, _pitStopTracker.LastStops),
                    out var standings))
            {
                _latestStandings = standings;
            }

            // The floating widget gets the compact focused view (podium + a block around the
            // player); the Dashboard has the room for the whole field, grouped by class. Only these
            // two pay for SOF — Relative pulls the running order out of this block but has no use
            // for the field strength.
            Feed(
                WidgetCatalog.Standings,
                Standings,
                () => (
                    Rows: _vm.StandingsOptions.ShowMulticlass
                        ? StandingsBuilder.BuildMulticlassView(_latestStandings, _vm.StandingsOptions.FocusSize)
                        : StandingsBuilder.BuildFocusedView(_latestStandings, _vm.StandingsOptions.FocusSize),
                    Sof: StandingsBuilder.ComputeStrengthOfField(session),
                    CarName: StandingsBuilder.SingleClassCarName(session)),
                (widget, view) =>
                {
                    widget.UpdateRows(view.Rows);
                    widget.SetSof(view.Sof);
                    widget.SetCarName(view.CarName);
                    widget.SetSessionId(session?.WeekendInfo?.SubSessionID ?? 0);
                },
                null);

            if (_dashboard is { } dashboard && _dashboardGuard.TryRun(
                    () => (
                        Rows: StandingsBuilder.GroupForDisplay(_latestStandings),
                        Sof: StandingsBuilder.ComputeStrengthOfField(session),
                        CarName: StandingsBuilder.SingleClassCarName(session)),
                    out var full))
            {
                _dashboardGuard.Run(() =>
                {
                    dashboard.UpdateStandingsRows(full.Rows);
                    dashboard.UpdateStandingsSof(full.Sof);
                    dashboard.UpdateStandingsCarName(full.CarName);
                });
            }
        }

        // Built every tick, unlike standings: Relative is about where cars are right now, and a
        // once-a-second refresh is visibly laggy when someone is alongside you.
        Feed(
            WidgetCatalog.Relative,
            Relative,
            () => StandingsBuilder.BuildRelative(telemetry, session, _vm.RelativeOptions.FocusSize, _latestStandings, _pitStopTracker.LastStops),
            (widget, rows) =>
            {
                widget.UpdateRows(rows);
                widget.SetCarName(StandingsBuilder.SingleClassCarName(session));
                widget.SetSessionId(session?.WeekendInfo?.SubSessionID ?? 0);
            },
            (dashboard, rows) => dashboard.UpdateRelativeRows(rows));

        Feed(
            WidgetCatalog.Flag,
            Flags,
            () => _flagPresenter.Present(FlagBuilder.Decode(telemetry), _vm.FlagOptions, TimeSpan.FromMilliseconds(Environment.TickCount64)),
            (widget, flags) => widget.UpdateState(flags),
            (dashboard, flags) => dashboard.UpdateFlag(flags));

        Feed(WidgetCatalog.TireInfo, Tires, () => TireInfoBuilder.Build(telemetry),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateTireInfo(state));

        Feed(WidgetCatalog.Delta, Delta, () => DeltaBuilder.Build(telemetry, _vm.DeltaReference),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateDelta(state));

        Feed(WidgetCatalog.Fuel, Fuel, () => _fuelBuilder.Build(telemetry),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateFuel(state));

        Feed(WidgetCatalog.FuelCalculator, FuelCalculator, () => _fuelCalculatorBuilder.Build(telemetry, session, _vm.FuelCalculatorOptions),
            (widget, state) => widget.UpdateState(state), null);

        Feed(WidgetCatalog.Incident, Incidents, () => IncidentBuilder.Build(telemetry, session),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateIncident(state));

        Feed(WidgetCatalog.TrackInfo, TrackInfo, () => TrackInfoBuilder.Build(telemetry, session),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateTrackInfo(state));

        // Every tick: the wind arrow follows the car's heading, which changes through every corner.
        Feed(WidgetCatalog.Weather, Weather, () => WeatherBuilder.Build(telemetry, session),
            (widget, state) => widget.UpdateState(state), null);

        Feed(WidgetCatalog.TrackMap, TrackMap, () => TrackMapBuilder.Build(telemetry, session),
            (widget, markers) => widget.UpdateState(markers), (dashboard, markers) => dashboard.UpdateTrackMap(markers));

        // Memory usage barely changes tick to tick — reuse the same once-a-second cadence as
        // Standings rather than recomputing it on every 100ms tick.
        if (_tickCount % StandingsUpdateEveryNTicks == 0)
        {
            _statusGuard.Run(UpdateDiagnostics);
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

        var session = _connection.Session;
        Feed(WidgetCatalog.Cockpit, Cockpit, () => CockpitBuilder.Build(telemetry, session),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateCockpit(state));

        Feed(WidgetCatalog.PedalTrace, Pedals, () => _pedalTraceBuilder.Build(telemetry),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdatePedalTrace(state));
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
            Clear(WidgetCatalog.Standings, Standings, w =>
            {
                w.SetCarName(carName);
                w.SetSessionId(subSessionId);
            });
        }
        else
        {
            Clear(WidgetCatalog.Relative, Relative, w =>
            {
                w.SetCarName(carName);
                w.SetSessionId(subSessionId);
            });
        }
    }

    private void ToggleDashboard()
    {
        if (_dashboard is { IsVisible: true })
        {
            AppLog.Activity("Dashboard", "Hidden");
            _dashboard.Hide();
            SetDashboardButtonCaption("Show dashboard");
            return;
        }

        var screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            AppLog.Warn("Dashboard", "No display found to show the dashboard on");
            return;
        }

        var display = Math.Clamp(_vm.SelectedMonitorIndex, 0, screens.Length - 1);
        AppLog.Activity("Dashboard", $"Shown on display {display + 1}");
        _dashboard ??= CreateDashboard();
        _dashboard.MoveToScreen(screens[display]);
        SetDashboardButtonCaption("Hide dashboard");
    }

    private DashboardWindow CreateDashboard()
    {
        var dashboard = new DashboardWindow();
        dashboard.SetFlagOptions(_vm.FlagOptions);
        dashboard.SetCockpitOptions(_vm.CockpitOptions);
        dashboard.Closed += (_, _) =>
        {
            // Closed with Alt+F4: a closed window can't be shown again, so the next toggle builds a new one.
            if (ReferenceEquals(_dashboard, dashboard))
            {
                AppLog.Activity("Dashboard", "Closed");
                _dashboard = null;
            }

            SetDashboardButtonCaption("Show dashboard");
        };
        return dashboard;
    }

    private void SetDashboardButtonCaption(string caption)
    {
        if (_vm.DashboardButton is { } button)
        {
            button.ButtonText = caption;
        }
    }

    // ===== Recovery =====

    /// <summary>A widget's guard tripped. Its state is rebuilt from scratch either way; the window
    /// is only replaced when drawing into it was what failed.</summary>
    private void RecoverWidget(string key, GuardStage stage)
    {
        switch (key)
        {
            case WidgetCatalog.Fuel:
                _fuelBuilder = new();
                break;
            case WidgetCatalog.FuelCalculator:
                _fuelCalculatorBuilder = new();
                break;
            case WidgetCatalog.PedalTrace:
                _pedalTraceBuilder = new();
                break;
            case WidgetCatalog.Flag:
                _flagPresenter = new();
                break;
        }

        if (stage == GuardStage.Render)
        {
            _vm.SlotOf(key).Restart();
        }
    }

    /// <summary>Replaces the dashboard window, reopening it where it was if it was on screen.</summary>
    private void RecoverDashboard()
    {
        if (_dashboard is not { } broken)
        {
            return;
        }

        var wasVisible = broken.IsVisible;
        _dashboard = null;
        try
        {
            broken.Close();
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            AppLog.Warn("Dashboard", "Could not close the failed dashboard window", e);
        }

        if (wasVisible)
        {
            ToggleDashboard();
        }
    }

    /// <summary>Last resort for a burst of unhandled UI exceptions: fresh windows for everything.</summary>
    private void RecoverOverlays()
    {
        _vm.RestartOverlays();
        RecoverDashboard();
    }

    /// <summary>
    /// Keys the per-session trackers (fuel history, pit stops, best laps, flag timers) to the event
    /// they were built from. A different event resets them, so a restarted sim never shows another
    /// car's fuel burn; the same event keeps them, so rejoining after an iRacing crash carries on.
    /// </summary>
    private void ObserveSession(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (ReferenceEquals(session, _observedSession) || session?.WeekendInfo is not { } weekend)
        {
            return;
        }

        _observedSession = session;
        var driverInfo = session.DriverInfo;
        var playerCar = driverInfo?.Drivers.FirstOrDefault(d => d.CarIdx == driverInfo.DriverCarIdx);
        var sessionType = _statusGuard.TryRun(() => CurrentSession.Entry(telemetry, session)?.SessionType, out var type) ? type : null;
        _sessionDescription = string.Join(" · ", new[]
        {
            weekend.TrackDisplayName ?? weekend.TrackName,
            string.IsNullOrEmpty(sessionType) ? null : sessionType,
            weekend.SubSessionID > 0 ? $"subsession {weekend.SubSessionID.ToString(CultureInfo.InvariantCulture)}" : "offline",
            playerCar?.CarScreenNameShort,
        }.Where(part => !string.IsNullOrEmpty(part)));
        AppLog.Session = _sessionDescription;

        var key = string.Create(CultureInfo.InvariantCulture, $"{weekend.SubSessionID}|{weekend.TrackName}|{playerCar?.CarID}");
        if (key == _sessionKey)
        {
            if (_awaitingReconnect)
            {
                _awaitingReconnect = false;
                AppLog.Info("Session", "Rejoined the same event; session state kept");
            }

            return;
        }

        var isNewEvent = _sessionKey is not null;
        _sessionKey = key;
        _awaitingReconnect = false;
        if (isNewEvent)
        {
            _fuelBuilder = new();
            _fuelCalculatorBuilder = new();
            _pedalTraceBuilder = new();
            _sessionBestLapTracker = new();
            _pitStopTracker = new();
            _flagPresenter = new();
            _latestStandings = [];
        }

        AppLog.Info("Session", isNewEvent ? "New event; per-session state reset" : "Session detected");
    }

    private void OnConnectionFault(object? sender, ConnectionFault fault)
    {
        // Raised on the reader thread; the logger is thread-safe and nothing else is touched here.
        var (level, message) = fault.Stage switch
        {
            "read" => (fault.ConsecutiveFailures >= 5 ? LogLevel.Error : LogLevel.Warning, "Telemetry read failed; retrying"),
            "watchdog" => (LogLevel.Warning, "No new telemetry; reopening shared memory"),
            "supervisor" => (LogLevel.Error, "Telemetry reader restarted"),
            _ when fault.Stage.StartsWith("subscriber", StringComparison.Ordinal) => (LogLevel.Error, "Telemetry event handler failed"),
            _ when fault.Stage.Contains("repaired", StringComparison.Ordinal) => (LogLevel.Info, "Session info repaired before parsing"),
            _ => (LogLevel.Warning, "Session info could not be fully parsed; keeping the last good values"),
        };

        var data = new Dictionary<string, string>
        {
            ["stage"] = fault.Stage,
            ["consecutive"] = fault.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture),
        };
        if (fault.RetryIn is { } retry)
        {
            data["retryInSeconds"] = retry.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        }

        if (AppLog.Current.Write(level, "Telemetry", message, fault.Exception, data)?.Ref is { } reference)
        {
            _telemetryErrorRef = reference;
        }
    }

    // ===== Health =====

    /// <summary>Once a second: roll every component into the health report, show it, and publish
    /// the snapshot a crash report would need.</summary>
    private void UpdateHealth()
    {
        var now = DateTime.UtcNow;
        var telemetry = _connection.Health;
        var sinceTick = telemetry.LastTickUtc is { } lastTick ? (now - lastTick).TotalSeconds : 0;
        var (status, summary) = telemetry.State switch
        {
            ConnectionState.Connected when telemetry.SessionInfoDegraded =>
                (HealthStatus.Degraded, "connected; session info needed repair"),
            ConnectionState.Connected => (HealthStatus.Healthy, "connected"),
            ConnectionState.Stale => (HealthStatus.Degraded, $"no new data for {sinceTick:0} s"),
            ConnectionState.Recovering when telemetry.ConsecutiveFailures >= 5 =>
                (HealthStatus.Failed, $"reconnecting after {telemetry.ConsecutiveFailures} errors"),
            ConnectionState.Recovering => (HealthStatus.Degraded, "reconnecting"),
            _ => (HealthStatus.Healthy, "waiting for iRacing"),
        };
        _health.Report("Telemetry", status, summary, telemetry.LastTickUtc, telemetry.LastErrorUtc, telemetry.TotalFailures, telemetry.LastError, _telemetryErrorRef);

        _health.Report("UI thread", _watchdog.Status, _watchdog.Status == HealthStatus.Healthy
            ? "responsive"
            : $"stalled {_watchdog.Stall.TotalSeconds:0} s");

        var settingsProblem = SettingsFile.LastWriteFailureUtc is { } settingsFailure && now - settingsFailure < RecentProblemWindow;
        _health.Report(
            "Settings",
            settingsProblem ? HealthStatus.Degraded : HealthStatus.Healthy,
            settingsProblem ? "a change could not be saved" : "saved",
            lastFailureUtc: SettingsFile.LastWriteFailureUtc,
            failures: SettingsFile.WriteFailures,
            lastError: SettingsFile.LastWriteError,
            lastErrorRef: SettingsFile.LastWriteErrorRef);

        // Updates never affect the overlay itself, so a failed check is only ever a degradation.
        _health.Report(
            "Updates",
            _updates.State == UpdateState.Failed ? HealthStatus.Degraded : HealthStatus.Healthy,
            _updates.Detail,
            failures: _updates.Failures,
            lastErrorRef: _updates.LastErrorRef);

        var uiProblem = GlobalExceptionHandler.LastHandledUtc is { } uiFailure && now - uiFailure < RecentProblemWindow;
        _health.Report(
            "Unhandled UI errors",
            uiProblem ? HealthStatus.Degraded : HealthStatus.Healthy,
            uiProblem ? $"{GlobalExceptionHandler.HandledCount} contained so far" : "none recently",
            lastFailureUtc: GlobalExceptionHandler.LastHandledUtc,
            failures: GlobalExceptionHandler.HandledCount,
            lastErrorRef: GlobalExceptionHandler.LastRef);

        var report = _health.Snapshot();
        _vm.Health = report.Overall;
        _vm.HealthLine = report.Summary;
        _vm.ConnectionState = telemetry.State;

        DiagnosticsReport.LatestContext = new DiagnosticsContext(
            _connection.IsConnected ? _sessionDescription : null,
            telemetry,
            _vm.Slots.Select(DescribeSlot).Append($"Dashboard: {(_dashboard is { IsVisible: true } ? "open" : "closed")}").ToList(),
            _vm.DiagnosticsLine);
    }

    private static string DescribeSlot(WidgetSlot slot) => string.Create(
        CultureInfo.InvariantCulture,
        $"{slot.Descriptor.Name}: {(slot.IsEnabled ? "on" : "off")}, {slot.StateLabel.ToLowerInvariant()}, size {slot.Scale}, opacity {slot.Opacity:P0}{(slot.HideOutsideCar ? ", hides outside car" : "")}");
}
