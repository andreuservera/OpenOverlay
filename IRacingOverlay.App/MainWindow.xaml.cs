using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IRacingOverlay.App.About;
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
    // Roughly once a second, without a second timer.
    private const int DiagnosticsUpdateEveryNTicks = 10;
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
    private readonly ComponentGuard _lineCrossingGuard;
    private readonly ComponentGuard _estTimeProfileGuard;
    private readonly ComponentGuard _penaltyGuard;
    private readonly ComponentGuard _lapLogGuard;
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
    // Driven by the telemetry reader rather than a DispatcherTimer: at 16 ms a DispatcherTimer lands
    // on the Windows timer grid (15.6/31.2 ms) at background priority, so the pedal trace sampled
    // unevenly and visibly stuttered. Not CompositionTarget.Rendering either: any handler there keeps
    // WPF's render thread composing on every monitor refresh for the life of the process, widgets
    // open or not — GPU time taken from iRacing. Each new telemetry tick posts at most one update.
    private readonly System.Diagnostics.Stopwatch _criticalClock = System.Diagnostics.Stopwatch.StartNew();
    private double _criticalIntervalMs = 100;
    private double _nextCriticalMs;
    private int _criticalTickPending;
    // PerfProbe: when the pending critical tick was posted, and the post-tick idle probe.
    private long _criticalPostedTicks;
    private PerfProbe.Mark _afterTickMark;
    private readonly Dictionary<string, (string Build, string Apply, string Dashboard)> _perfNames = new();

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
        ShowUpdateNoticeIfPending();
    }

    /// <summary>
    /// The one-time notice after an update, over the control panel. It waits for the panel to be on
    /// screen — from the tray, the next time it's opened — and is marked shown before it appears,
    /// so not even a crash brings it back a second time.
    /// </summary>
    private void ShowUpdateNoticeIfPending()
    {
        if (!InstallHistory.NoticePending || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        InstallHistory.MarkNoticeShown();
        try
        {
            var notice = new UpdateNoticeWindow(UpdateNotice.For(BuildInfo.Version, ReleaseCatalog.Current))
            {
                Owner = this,
                ShowActivated = IsActive,
            };
            notice.DetailsRequested += () =>
            {
                RestoreFromTray();
                _vm.ShowWhatsNew();
            };
            notice.Show();
            AppLog.Info("Updates", "Update notice shown", new Dictionary<string, string> { ["version"] = BuildInfo.Version });
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            AppLog.Warn("Updates", "Update notice could not be shown", e);
        }
    }

    private void ExitApplication()
    {
        // The layout editor may hold unsaved changes; the user can still back out of exiting.
        if (!_vm.CloseLayoutEditor())
        {
            return;
        }

        AppLog.Activity("Control Panel", "Exit requested");
        _exiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting || TrayPreferencesStore.CloseBehavior == CloseBehavior.Exit)
        {
            // A first close with "Exit application" set: the layout editor gets its say before the
            // app goes. ExitApplication has already asked, and session end must not wait.
            if (!_exiting && !_vm.CloseLayoutEditor())
            {
                e.Cancel = true;
                return;
            }

            if (!_exiting)
            {
                AppLog.Activity("Control Panel", "Closed; exiting");
            }

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
    // The player's racing laps, observed every tick whatever is open: the fuel calculator averages it
    // and the session clock prices a lap with it. Set up by ResetLapHistory.
    private LapLog _lapLog = null!;

    // What the driver tables' headers last showed, so switching a header field back on can put it
    // up at once instead of waiting for the next tick.
    private string _sessionType = "";
    private double _strengthOfField;
    private FuelCalculatorBuilder _fuelCalculatorBuilder = null!;
    // Filled on the telemetry thread, where every tick is seen: PlayerIncidents may last just one.
    private readonly IncidentReportLatch _incidentReports = new();
    private SessionBestLapTracker _sessionBestLapTracker = new();
    private PitStopTracker _pitStopTracker = new();
    private LineCrossingTracker _lineCrossings = new();
    private EstTimeProfile _estTimeProfile = new();
    private PenaltyFlagTracker _penaltyTracker = new();
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

    // What the cockpit was last fed; a repeat means a frame with no new sim tick, so nothing to redraw.
    private (int Tick, CockpitWidget? Widget, DashboardWindow? Dashboard)? _lastCockpitFeed;

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
        Preview.Bind(_vm.StandingsOptions, _vm.RelativeOptions, _vm.FuelCalculatorOptions, _vm.FlagOptions, _vm.FlagPreview, _vm.CockpitOptions, _vm.WeatherOptions, _vm.TrackInfoOptions, _vm.PedalTraceOptions);
        DataContext = _vm;

        foreach (var descriptor in WidgetCatalog.All)
        {
            var key = descriptor.Key;
            _widgetGuards[key] = _health.CreateGuard($"Widget: {descriptor.Name}", stage => RecoverWidget(key, stage));
        }

        _dashboardGuard = _health.CreateGuard("Dashboard", _ => RecoverDashboard());
        _standingsGuard = _health.CreateGuard("Standings model", _ => _sessionBestLapTracker = new());
        _pitStopGuard = _health.CreateGuard("Pit stop tracker", _ => _pitStopTracker = new());
        _lineCrossingGuard = _health.CreateGuard("Line crossing timing", _ => _lineCrossings = new());
        _estTimeProfileGuard = _health.CreateGuard("Relative time curve", _ => _estTimeProfile = new());
        _penaltyGuard = _health.CreateGuard("Penalty flag log", _ => _penaltyTracker = new());
        _lapLogGuard = _health.CreateGuard("Lap log", _ => ResetLapHistory());
        ResetLapHistory();
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
        _connection.TelemetryUpdated += (_, snapshot) => _incidentReports.Observe(snapshot);
        _connection.TelemetryUpdated += (_, _) => PostCriticalTick();
        _connection.Connected += (_, _) =>
        {
            AppLog.Info("Telemetry", "Connected to iRacing");
            Dispatcher.BeginInvoke(() => _vm.IsConnected = true);
        };
        _connection.Disconnected += (_, _) =>
        {
            AppLog.Info("Telemetry", "Disconnected from iRacing");
            _incidentReports.Reset();
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
        // A layout switched to by shortcut is announced from the tray: the sim has the screen.
        _vm.LayoutSwitchedNotice += (title, text) => _tray?.ShowNotice(title, text);

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
        ContentRendered += (_, _) => ShowUpdateNoticeIfPending();
        // Signing out or shutting down must never be held up by the close-to-tray behavior.
        System.Windows.Application.Current.SessionEnding += (_, _) => _exiting = true;

        Closed += (_, _) =>
        {
            // The watchdog first: once the loop stops beating, a live watchdog would call it a hang.
            _watchdog.Dispose();
            _uiTimer.Stop();
            GlobalExceptionHandler.StormRecovery = null;
            _updates.Dispose();
            _hotkeys?.Dispose();
            _tray?.Dispose();
            _tray = null;
            _connection.Stop();
            _vm.CloseAllWidgets();
            _dashboard?.Close();

            // The app ends with this window (ShutdownMode=OnMainWindowClose). Anything still open
            // here used to keep the process alive with no window and no tray icon; it is closed
            // by the shutdown now, and named so the leak can be found.
            var leftovers = System.Windows.Application.Current.Windows.OfType<Window>()
                .Where(w => !ReferenceEquals(w, this))
                .Select(w => w.GetType().Name)
                .ToList();
            if (leftovers.Count > 0)
            {
                AppLog.Warn("Shutdown", $"Windows still open after the control panel closed: {string.Join(", ", leftovers)}");
            }
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
        _lastCockpitFeed = null;
        _vm.TelemetryLine = "Waiting for iRacing";

        _sessionType = "";
        _strengthOfField = 0;
        Clear(WidgetCatalog.Relative, Relative, w =>
        {
            w.UpdateRows([]);
            w.SetSof(0);
            w.SetProgress(SessionProgress.Empty);
            w.SetConditions(TableConditions.Empty);
        });
        Clear(WidgetCatalog.Standings, Standings, w =>
        {
            w.UpdateRows([]);
            w.SetSof(0);
            w.SetProgress(SessionProgress.Empty);
            w.SetConditions(TableConditions.Empty);
        });
        Clear(WidgetCatalog.Cockpit, Cockpit, w => w.UpdateState(CockpitState.Empty));
        Clear(WidgetCatalog.Flag, Flags, w => w.UpdateState([FlagState.None]));
        Clear(WidgetCatalog.TireInfo, Tires, w => w.UpdateState(TireInfoState.Empty));
        Clear(WidgetCatalog.Delta, Delta, w => w.UpdateState(DeltaState.Empty));
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
                dashboard.UpdateSessionProgress(SessionProgress.Empty);
                dashboard.UpdateCockpit(CockpitState.Empty);
                dashboard.UpdateFlag([FlagState.None]);
                dashboard.UpdateTireInfo(TireInfoState.Empty);
                dashboard.UpdateDelta(DeltaState.Empty);
                dashboard.UpdateFuelCalculator(FuelCalculatorState.Empty);
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

        if (!_perfNames.TryGetValue(key, out var names))
        {
            names = ($"{key}.build", $"{key}.apply", $"{key}.dashboard");
            _perfNames[key] = names;
        }

        var guard = _widgetGuards[key];
        var mark = PerfProbe.Begin();
        if (!guard.TryRun(build, out var state))
        {
            return;
        }

        PerfProbe.End(names.Build, mark);

        if (widget is not null)
        {
            mark = PerfProbe.Begin();
            guard.Run(() => toWidget(widget, state));
            PerfProbe.End(names.Apply, mark);
        }

        if (dashboard is not null)
        {
            mark = PerfProbe.Begin();
            _dashboardGuard.Run(() => toDashboard!(dashboard, state));
            PerfProbe.End(names.Dashboard, mark);
        }
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        _watchdog.Beat();
        _uiTickStopwatch.Restart();
        var perfMark = PerfProbe.Begin();
        try
        {
            UiTimer_TickCore();
        }
        finally
        {
            PerfProbe.End("ui.tick", perfMark);
            if (PerfProbe.Enabled)
            {
                // Whatever the tick queued (layout, render, input) runs before ContextIdle: the gap
                // to this callback is the WPF work the tick itself doesn't see.
                _afterTickMark = PerfProbe.Begin();
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => PerfProbe.End("ui.after-tick (layout+render)", _afterTickMark));
                PerfProbe.MaybeFlush();
            }

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
        var trackersMark = PerfProbe.Begin();
        // Every tick, whatever is open: a stop is timed on entry and exit, and a missed edge loses it.
        _pitStopGuard.Run(() => _pitStopTracker.Update(telemetry, session), GuardStage.Build);
        // Every tick too: a crossing is timed from the ticks either side of the line, and the time
        // curve is learned from wherever the cars are each tick.
        _lineCrossingGuard.Run(() => _lineCrossings.Update(telemetry, session), GuardStage.Build);
        _estTimeProfileGuard.Run(() => _estTimeProfile.Update(telemetry, session), GuardStage.Build);
        _lapLogGuard.Run(() => _lapLog.Observe(telemetry), GuardStage.Build);
        _penaltyGuard.Run(() =>
        {
            foreach (var (driver, penalties) in _penaltyTracker.Update(telemetry, session))
            {
                AppLog.Activity("Flags", "Penalty flags changed", new Dictionary<string, string>
                {
                    ["car"] = driver.CarNumber,
                    ["carIdx"] = driver.CarIdx.ToString(CultureInfo.InvariantCulture),
                    ["player"] = (driver.CarIdx == session?.DriverInfo?.DriverCarIdx).ToString(),
                    ["black"] = penalties.Black.ToString(),
                    ["furled"] = penalties.Furled.ToString(),
                    ["meatball"] = penalties.Meatball.ToString(),
                });
            }
        }, GuardStage.Build);
        PerfProbe.End("trackers", trackersMark);
        // Relative needs the standings order too, for its POS and iRΔ columns, so this runs
        // whenever any of the three consumers is open — not just the two that display it directly.
        // Every tick: the classification itself only moves as cars cross the line, but pit road,
        // flags and tyres are live and should show the moment they change.
        var needsStandings = Standings is not null || _dashboard is not null || Relative is not null;
        if (needsStandings)
        {
            // A failed rebuild keeps the last good order: a table a tick old beats an empty one.
            var standingsMark = PerfProbe.Begin();
            if (_standingsGuard.TryRun(
                    () => StandingsBuilder.BuildStandings(
                        telemetry, session, _sessionBestLapTracker, _pitStopTracker.LastStops, _lineCrossings),
                    out var standings))
            {
                _latestStandings = standings;
            }

            PerfProbe.End("standings.order", standingsMark);

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
                    SessionType: StandingsBuilder.SessionTypeLabel(telemetry, session)),
                (widget, view) =>
                {
                    widget.UpdateRows(view.Rows);
                    _sessionType = view.SessionType;
                    _strengthOfField = view.Sof;
                    widget.SetSof(view.Sof);
                    widget.SetSessionType(view.SessionType);
                },
                null);

            if (_dashboard is { } dashboard && _dashboardGuard.TryRun(
                    () => (
                        Rows: StandingsBuilder.GroupForDisplay(_latestStandings),
                        Sof: StandingsBuilder.ComputeStrengthOfField(session),
                        SessionType: StandingsBuilder.SessionTypeLabel(telemetry, session)),
                    out var full))
            {
                _dashboardGuard.Run(() =>
                {
                    dashboard.UpdateStandingsRows(full.Rows);
                    dashboard.UpdateStandingsSof(full.Sof);
                    dashboard.UpdateStandingsSessionType(full.SessionType);
                });
            }
        }

        // Built every tick: Relative is about where cars are right now, and a once-a-second refresh
        // is visibly laggy when someone is alongside you.
        Feed(
            WidgetCatalog.Relative,
            Relative,
            () => StandingsBuilder.BuildRelative(
                telemetry, session, _vm.RelativeOptions.FocusSize, _latestStandings, _pitStopTracker.LastStops, _estTimeProfile),
            (widget, rows) =>
            {
                widget.UpdateRows(rows);
                _sessionType = StandingsBuilder.SessionTypeLabel(telemetry, session);
                _strengthOfField = StandingsBuilder.ComputeStrengthOfField(session);
                widget.SetSessionType(_sessionType);
                widget.SetSof(_strengthOfField);
            },
            (dashboard, rows) => dashboard.UpdateRelativeRows(rows));

        // The tables' session information: the clock, brake bias, conditions and incidents move every tick, so
        // not on the once-a-second standings beat.
        Feed(
            WidgetCatalog.Standings,
            Standings,
            () => (Progress: SessionProgressBuilder.Build(telemetry, session, _lapLog.RecentLapSeconds()), Conditions: TableConditions.Build(telemetry, session)),
            (widget, info) =>
            {
                widget.SetProgress(info.Progress);
                widget.SetConditions(info.Conditions);
            },
            (dashboard, info) => dashboard.UpdateSessionProgress(info.Progress));
        Feed(
            WidgetCatalog.Relative,
            Relative,
            () => (Progress: SessionProgressBuilder.Build(telemetry, session, _lapLog.RecentLapSeconds()), Conditions: TableConditions.Build(telemetry, session)),
            (widget, info) =>
            {
                widget.SetProgress(info.Progress);
                widget.SetConditions(info.Conditions);
            },
            null);

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

        Feed(WidgetCatalog.FuelCalculator, FuelCalculator, () => _fuelCalculatorBuilder.Build(telemetry, session, _vm.FuelCalculatorOptions),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateFuelCalculator(state));

        Feed(WidgetCatalog.Incident, Incidents, () => IncidentBuilder.Build(telemetry, session, _incidentReports.Latest),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateIncident(state));

        Feed(WidgetCatalog.TrackInfo, TrackInfo, () => TrackInfoBuilder.Build(telemetry, session, _lapLog.RecentLapSeconds()),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateTrackInfo(state));

        // Every tick: the wind arrow follows the car's heading, which changes through every corner.
        Feed(WidgetCatalog.Weather, Weather, () => WeatherBuilder.Build(telemetry, session),
            (widget, state) => widget.UpdateState(state), null);

        Feed(WidgetCatalog.TrackMap, TrackMap, () => TrackMapBuilder.Build(telemetry, session),
            (widget, markers) => widget.UpdateState(markers), (dashboard, markers) => dashboard.UpdateTrackMap(markers));

        // Memory usage barely changes tick to tick: once a second rather than on every 100ms tick.
        if (_tickCount % DiagnosticsUpdateEveryNTicks == 0)
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

    /// <summary>Called on the telemetry thread for every new tick. Coalesced: while one update is
    /// still queued on the UI thread, later ticks don't pile up behind it.</summary>
    private void PostCriticalTick()
    {
        if (Interlocked.Exchange(ref _criticalTickPending, 1) == 1)
        {
            return;
        }

        Volatile.Write(ref _criticalPostedTicks, System.Diagnostics.Stopwatch.GetTimestamp());

        Dispatcher.InvokeAsync(OnTelemetryTick, DispatcherPriority.Render);
    }

    private void OnTelemetryTick()
    {
        Volatile.Write(ref _criticalTickPending, 0);
        if (PerfProbe.Enabled)
        {
            // How long the UI thread kept a new telemetry tick waiting: the source of the gaps.
            var waited = System.Diagnostics.Stopwatch.GetElapsedTime(Volatile.Read(ref _criticalPostedTicks));
            PerfProbe.Record("critical.queue-wait", waited.TotalMilliseconds);
        }

        // A few ms of slack, so a 16 ms target runs on every 60 Hz telemetry tick (16.7 ms apart).
        var now = _criticalClock.Elapsed.TotalMilliseconds;
        if (now < _nextCriticalMs - 3)
        {
            return;
        }

        _nextCriticalMs = now + _criticalIntervalMs;
        CriticalTimer_Tick(this, EventArgs.Empty);
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
        var perfMark = PerfProbe.Begin();
        try
        {
            CriticalTimer_TickCore();
        }
        finally
        {
            PerfProbe.End("critical.tick", perfMark);
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
        var cockpit = Cockpit;
        var cockpitFeed = (telemetry.TickCount, cockpit, _dashboard);
        if (cockpitFeed != _lastCockpitFeed)
        {
            _lastCockpitFeed = cockpitFeed;
            Feed(WidgetCatalog.Cockpit, cockpit, () => CockpitBuilder.Build(telemetry, session),
                (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateCockpit(state));
        }

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

        // Rolling ~1s window (this is called once every DiagnosticsUpdateEveryNTicks UI ticks) rather
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
        if (table == DriverTable.Standings)
        {
            Clear(WidgetCatalog.Standings, Standings, w =>
            {
                w.SetSessionType(_sessionType);
                w.SetSof(_strengthOfField);
            });
        }
        else
        {
            Clear(WidgetCatalog.Relative, Relative, w =>
            {
                w.SetSessionType(_sessionType);
                w.SetSof(_strengthOfField);
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
        dashboard.SetFuelCalculatorOptions(_vm.FuelCalculatorOptions);
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
            case WidgetCatalog.FuelCalculator:
                _fuelCalculatorBuilder = new(_lapLog);
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

    private void ResetLapHistory()
    {
        _lapLog = new();
        _fuelCalculatorBuilder = new(_lapLog);
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
            ResetLapHistory();
            _pedalTraceBuilder = new();
            _sessionBestLapTracker = new();
            _pitStopTracker = new();
            _lineCrossings = new();
            _estTimeProfile = new();
            _penaltyTracker = new();
            _flagPresenter = new();
            _latestStandings = [];
        }

        AppLog.Info("Session", isNewEvent ? "New event; per-session state reset" : "Session detected", new Dictionary<string, string>
        {
            ["perCarFlags"] = telemetry.HasVariable(TelemetryVarNames.CarIdxSessionFlags).ToString(),
        });
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
        _vm.UpdateStatus = _updates.Detail;

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
