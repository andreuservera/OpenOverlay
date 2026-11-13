using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IRacingOverlay.App.About;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Dashboard;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.TrackMaps;
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
    // Track and session type alone, for the control panel's status pill.
    private string? _sessionLabel;
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
        ApplyPaneSplit(PaneSplitStore.ConfigShare);
        PaneSplitter.DragCompleted += (_, _) => SavePaneSplit();

        // Before DataContext, so the preview already knows which options objects to follow by the
        // time the Slot binding hands it its first widget.
        Preview.Bind(_vm.StandingsOptions, _vm.RelativeOptions, _vm.FuelCalculatorOptions, _vm.FlagOptions, _vm.FlagPreview, _vm.CockpitOptions, _vm.WeatherOptions, _vm.TrackInfoOptions, _vm.PedalTraceOptions, _vm.PreviewSettings);
        DataContext = _vm;

        foreach (var descriptor in WidgetCatalog.All)
        {
            var key = descriptor.Key;
            _widgetGuards[key] = _health.CreateGuard($"Widget: {descriptor.LogName}", stage => RecoverWidget(key, stage));
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

        // The coach only listens while live coaching is switched on in its page.
        IRacingOverlay.Coaching.Ui.LiveCoaching.Log += line => AppLog.Info("Coaching", line);
        IRacingOverlay.Coaching.Ui.LiveCoaching.Attach(_connection);
        _connection.Fault += OnConnectionFault;
        _connection.TelemetryUpdated += (_, snapshot) => _incidentReports.Observe(snapshot);
        _connection.TelemetryUpdated += (_, snapshot) => ObserveTrackOutline(snapshot);
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
        _vm.RevealRequested += RevealSetting;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ControlPanelViewModel.ShowsPreviewPlaceholder))
            {
                FitPanesToPage();
            }
            else if (e.PropertyName is nameof(ControlPanelViewModel.Selected) or nameof(ControlPanelViewModel.SelectedTab))
            {
                // A new page starts at its top. A search result scrolls to its setting afterwards,
                // once the page has been laid out.
                SettingsScroller.ScrollToTop();
            }
        };
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
                Loc.T("tray.restarted"),
                Loc.T("tray.restarted.body"));
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
        // A fresh install is offered the setup assistant once, over the panel, as it first appears.
        ContentRendered += (_, _) => _vm.OfferSetupOnFirstRun();
        // Signing out or shutting down must never be held up by the close-to-tray behavior.
        System.Windows.Application.Current.SessionEnding += (_, _) => _exiting = true;

        Closed += (_, _) =>
        {
            // The watchdog first: once the loop stops beating, a live watchdog would call it a hang.
            // Each step is named for the shutdown guard, so a stuck exit says where it stuck.
            ShutdownGuard.Step("UI watchdog");
            _watchdog.Dispose();
            _uiTimer.Stop();
            GlobalExceptionHandler.StormRecovery = null;
            ShutdownGuard.Step("update service");
            _updates.Dispose();
            ShutdownGuard.Step("hotkeys");
            _hotkeys?.Dispose();
            ShutdownGuard.Step("tray icon");
            _tray?.Dispose();
            _tray = null;
            ShutdownGuard.Step("live coaching");
            IRacingOverlay.Coaching.Ui.LiveCoaching.Detach();
            ShutdownGuard.Step("replay");
            ControlPanelViewModel.StopReplay();
            ShutdownGuard.Step("iRacing connection");
            _connection.Stop();
            ShutdownGuard.Step("widgets");
            _vm.CloseAllWidgets();
            ShutdownGuard.Step("dashboard");
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

            ShutdownGuard.Step("application shutdown (after the control panel closed)");
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
    private RadarWidget? Radar => _vm.WidgetOf<RadarWidget>(WidgetCatalog.Radar);
    private CircuitMapWidget? CircuitMap => _vm.WidgetOf<CircuitMapWidget>(WidgetCatalog.CircuitMap);
    private FuelCalculatorWidget? FuelCalculator => _vm.WidgetOf<FuelCalculatorWidget>(WidgetCatalog.FuelCalculator);

    // Learns and keeps the current track's shape for the circuit map.
    private readonly TrackOutlineLearner _trackOutlines = new();

    // Logged as it changes, while drivers get in before the green, to confirm live that every car
    // on the qualifying grid is in the roster from the start, so Standings can list it.
    private PreGreenGrid? _lastPreGreenGrid;
    private Action<PreGreenGrid>? _logPreGreenGrid; // one delegate, not one per tick

    // The two panes' widths as last set, for putting them back (see MainWindow.Shell.cs).
    private (GridLength Config, GridLength Preview)? _paneWidths;
}
