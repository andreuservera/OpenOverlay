using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IRacingOverlay.App.Dashboard;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;
using IRacingOverlay.Sdk;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App;

public partial class MainWindow : Window
{
    // Standings only needs to feel "live," not sub-second precise — recomputing every 100ms was
    // wasted work (and, before the continuous-ordering fix, it happened to disguise a bug: since the
    // underlying data barely changed within a lap either way, it *looked* like updates only landed
    // at lap boundaries). This throttles it to roughly once a second without a second timer.
    private const int StandingsUpdateEveryNTicks = 10;

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
    // possible causes and need to be told apart. Reset once a second in UpdateMemoryText.
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

    private RelativeWidget? _relativeWidget;
    private StandingsWidget? _standingsWidget;
    private CockpitWidget? _cockpitWidget;
    private FlagWidget? _flagWidget;
    private TireInfoWidget? _tireInfoWidget;
    private DeltaWidget? _deltaWidget;
    private FuelWidget? _fuelWidget;
    private PedalTraceWidget? _pedalTraceWidget;
    private IncidentWidget? _incidentWidget;
    private TrackInfoWidget? _trackInfoWidget;
    private TrackMapWidget? _trackMapWidget;
    private FuelCalculatorWidget? _fuelCalculatorWidget;
    private DashboardWindow? _dashboard;
    private DeltaReference _deltaReference = DeltaReference.SessionBest;
    private readonly StandingsColumnVisibility _standingsColumnVisibility = new();
    private readonly FuelCalculatorOptions _fuelCalculatorOptions = new();

    public MainWindow()
    {
        // Read both stores *before* InitializeComponent(): every ComboBoxItem in MainWindow.xaml
        // that has IsSelected="True" (the XAML-declared first-run default) fires that ComboBox's
        // SelectionChanged handler the moment InitializeComponent() constructs it — and both
        // handlers below immediately persist whatever's currently selected. Reading the previous
        // launch's saved value first means we still have it in hand even though InitializeComponent
        // is about to overwrite the file on disk with the XAML default; the actual restore
        // assignment further down fires SelectionChanged again and writes the real value back.
        var savedDashboardTheme = DashboardThemeStore.Get();
        var savedCriticalRefreshIndex = CriticalRefreshStore.Get();

        // Same trap, one step worse: the fuel average-source ComboBox's handler persists the *whole*
        // options object, so the startup event stamped the constructor defaults over every section
        // toggle the user had saved — before RestoreFuelCalculatorOptions ever got to read them back.
        // Loading into the object up front makes that write a no-op instead.
        FuelCalculatorOptionsStore.ApplyTo(_fuelCalculatorOptions);
        var savedFuelAverageSource = _fuelCalculatorOptions.AverageSource;

        InitializeComponent();

        MonitorComboBox.ItemsSource = Screen.AllScreens;
        MonitorComboBox.DisplayMemberPath = "DeviceName";
        MonitorComboBox.SelectedIndex = Screen.AllScreens.Length > 1 ? 1 : 0;

        DashboardThemeComboBox.SelectedIndex = savedDashboardTheme switch
        {
            DashboardTheme.DigitalHud => 1,
            DashboardTheme.RawDiy => 2,
            _ => 0,
        };

        CriticalRefreshComboBox.SelectedIndex = savedCriticalRefreshIndex;

        _connection.Connected += (_, _) => Dispatcher.BeginInvoke(() => SetStatus(connected: true));
        _connection.Disconnected += (_, _) => Dispatcher.BeginInvoke(() => SetStatus(connected: false));

        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        _criticalTimer.Tick += CriticalTimer_Tick;
        _criticalTimer.Start();

        Closed += (_, _) =>
        {
            _uiTimer.Stop();
            _criticalTimer.Stop();
            _connection.Stop();
            _relativeWidget?.Close();
            _standingsWidget?.Close();
            _cockpitWidget?.Close();
            _flagWidget?.Close();
            _tireInfoWidget?.Close();
            _deltaWidget?.Close();
            _fuelWidget?.Close();
            _pedalTraceWidget?.Close();
            _incidentWidget?.Close();
            _trackInfoWidget?.Close();
            _trackMapWidget?.Close();
            _fuelCalculatorWidget?.Close();
            _dashboard?.Close();
        };

        RestoreWidgetVisibility();
        RestoreAutoHideCheckboxes();
        RestoreStandingsColumnVisibility();
        RestoreFuelCalculatorOptions(savedFuelAverageSource);

        _connection.Start();
    }

    /// <summary>Loads persisted Standings column toggles into both the checkboxes and the shared
    /// StandingsColumnVisibility instance the overlay widget's panel binds to — the Dashboard's own
    /// panel instance never sees this object, so it always shows every column.</summary>
    private void RestoreStandingsColumnVisibility()
    {
        StandingsColumnVisibilityStore.ApplyTo(_standingsColumnVisibility);
        StandingsIRatingCheckBox.IsChecked = _standingsColumnVisibility.ShowIRating;
        StandingsIRatingDeltaCheckBox.IsChecked = _standingsColumnVisibility.ShowIRatingDelta;
        StandingsLicenseCheckBox.IsChecked = _standingsColumnVisibility.ShowLicense;
        StandingsLapCheckBox.IsChecked = _standingsColumnVisibility.ShowLap;
        StandingsLastLapCheckBox.IsChecked = _standingsColumnVisibility.ShowLastLap;
        StandingsBestLapCheckBox.IsChecked = _standingsColumnVisibility.ShowBestLap;
        StandingsGapCheckBox.IsChecked = _standingsColumnVisibility.ShowGap;
    }

    /// <summary>Pushes the already-loaded Fuel Calculator settings into the control-panel inputs.
    /// The values were read in the constructor before InitializeComponent, so this only mirrors the
    /// options object into the UI — the assignments re-fire their handlers, which write the same
    /// values straight back, keeping restore and user-edit on one path. Average source is passed in
    /// separately because InitializeComponent's own SelectionChanged has already reset it.</summary>
    private void RestoreFuelCalculatorOptions(FuelAverageSource savedAverageSource)
    {
        FuelCalcBarCheckBox.IsChecked = _fuelCalculatorOptions.ShowFuelBar;
        FuelCalcRemainingCheckBox.IsChecked = _fuelCalculatorOptions.ShowFuelRemaining;
        FuelCalcLastLapCheckBox.IsChecked = _fuelCalculatorOptions.ShowLastLap;
        FuelCalcAverageCheckBox.IsChecked = _fuelCalculatorOptions.ShowAverage;
        FuelCalcMinimumCheckBox.IsChecked = _fuelCalculatorOptions.ShowMinimum;
        FuelCalcMaximumCheckBox.IsChecked = _fuelCalculatorOptions.ShowMaximum;
        FuelCalcLapsRemainingCheckBox.IsChecked = _fuelCalculatorOptions.ShowLapsRemaining;
        FuelCalcToFinishCheckBox.IsChecked = _fuelCalculatorOptions.ShowFuelToFinish;
        FuelCalcRefuelCheckBox.IsChecked = _fuelCalculatorOptions.ShowRefuel;

        _fuelCalculatorOptions.AverageSource = savedAverageSource;
        FuelAverageSourceComboBox.SelectedIndex = (int)savedAverageSource;

        FuelMarginLapsTextBox.Text = _fuelCalculatorOptions.MarginLaps.ToString(CultureInfo.InvariantCulture);
        FuelMarginLitersTextBox.Text = _fuelCalculatorOptions.MarginLiters.ToString(CultureInfo.InvariantCulture);

        FuelCalculatorOptionsStore.Save(_fuelCalculatorOptions);
    }

    /// <summary>Re-checks whichever overlay checkboxes were checked last run — each CheckBox's
    /// Checked event handler (already wired via XAML) does the actual widget-creation/Show() work,
    /// so setting IsChecked here is enough; unchanged (false->false) values don't re-fire it.</summary>
    private void RestoreWidgetVisibility()
    {
        RelativeCheckBox.IsChecked = WidgetVisibilityStore.Get("Relative");
        StandingsCheckBox.IsChecked = WidgetVisibilityStore.Get("Standings");
        CockpitCheckBox.IsChecked = WidgetVisibilityStore.Get("Cockpit");
        FlagCheckBox.IsChecked = WidgetVisibilityStore.Get("Flag");
        TireInfoCheckBox.IsChecked = WidgetVisibilityStore.Get("TireInfo");
        DeltaCheckBox.IsChecked = WidgetVisibilityStore.Get("Delta");
        FuelCheckBox.IsChecked = WidgetVisibilityStore.Get("Fuel");
        PedalTraceCheckBox.IsChecked = WidgetVisibilityStore.Get("PedalTrace");
        IncidentCheckBox.IsChecked = WidgetVisibilityStore.Get("Incident");
        TrackInfoCheckBox.IsChecked = WidgetVisibilityStore.Get("TrackInfo");
        TrackMapCheckBox.IsChecked = WidgetVisibilityStore.Get("TrackMap");
        FuelCalculatorCheckBox.IsChecked = WidgetVisibilityStore.Get("FuelCalculator");
    }

    /// <summary>Re-checks whichever "hide outside car" checkboxes were checked last run — each one
    /// shares the single AutoHideCheckBox_Changed handler (wired via XAML) keyed off its Tag, so
    /// setting IsChecked here is enough to both restore and persist the same value.</summary>
    private void RestoreAutoHideCheckboxes()
    {
        RelativeAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("Relative");
        StandingsAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("Standings");
        CockpitAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("Cockpit");
        FlagAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("Flag");
        TireInfoAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("TireInfo");
        DeltaAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("Delta");
        FuelAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("Fuel");
        PedalTraceAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("PedalTrace");
        IncidentAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("Incident");
        TrackInfoAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("TrackInfo");
        TrackMapAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("TrackMap");
        FuelCalculatorAutoHideCheckBox.IsChecked = HideOutsideCarStore.Get("FuelCalculator");
    }

    private void SetStatus(bool connected)
    {
        StatusDot.Fill = connected ? Brushes.LimeGreen : Brushes.Red;
        StatusText.Text = connected ? "Connected to iRacing" : "Waiting for iRacing…";
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
        }
    }

    private void UiTimer_TickCore()
    {
        var telemetry = _connection.Latest;
        if (telemetry is null)
        {
            return;
        }

        UpdateDebugText(telemetry);

        // IsOnTrack is false at the main menu, on a garage/setup screen, spectating, or watching a
        // replay — true only once the player is actually in the car with physics running. A missing
        // variable defaults to "driving" (don't hide anything) rather than risk hiding widgets from a
        // false read.
        var playerNotDriving = telemetry.HasVariable(TelemetryVarNames.IsOnTrack) && !telemetry.GetBool(TelemetryVarNames.IsOnTrack);
        ApplyAutoHideVisibility(playerNotDriving);

        var session = _connection.Session;
        var relativeRows = StandingsBuilder.BuildRelative(telemetry, session);
        _relativeWidget?.UpdateRows(relativeRows);
        _dashboard?.UpdateRelativeRows(relativeRows);

        _tickCount++;
        if ((_standingsWidget is not null || _dashboard is not null) && _tickCount % StandingsUpdateEveryNTicks == 0)
        {
            var standingsRows = StandingsBuilder.BuildStandings(telemetry, session, _sessionBestLapTracker);
            var standingsDisplay = StandingsBuilder.GroupForDisplay(standingsRows);
            var sof = StandingsBuilder.ComputeStrengthOfField(session);
            _standingsWidget?.UpdateRows(standingsDisplay);
            _standingsWidget?.SetSof(sof);
            _dashboard?.UpdateStandingsRows(standingsDisplay);
            _dashboard?.UpdateStandingsSof(sof);
        }

        if (_flagWidget is not null || _dashboard is not null)
        {
            var flagStates = FlagBuilder.Build(telemetry);
            _flagWidget?.UpdateState(flagStates);
            _dashboard?.UpdateFlag(flagStates);
        }

        if (_tireInfoWidget is not null || _dashboard is not null)
        {
            var tireInfoState = TireInfoBuilder.Build(telemetry);
            _tireInfoWidget?.UpdateState(tireInfoState);
            _dashboard?.UpdateTireInfo(tireInfoState);
        }

        if (_deltaWidget is not null || _dashboard is not null)
        {
            var deltaState = DeltaBuilder.Build(telemetry, _deltaReference);
            _deltaWidget?.UpdateState(deltaState);
            _dashboard?.UpdateDelta(deltaState);
        }

        if (_fuelWidget is not null || _dashboard is not null)
        {
            var fuelState = _fuelBuilder.Build(telemetry);
            _fuelWidget?.UpdateState(fuelState);
            _dashboard?.UpdateFuel(fuelState);
        }

        if (_fuelCalculatorWidget is not null)
        {
            _fuelCalculatorWidget.UpdateState(_fuelCalculatorBuilder.Build(telemetry, session, _fuelCalculatorOptions));
        }

        if (_incidentWidget is not null || _dashboard is not null)
        {
            var incidentState = IncidentBuilder.Build(telemetry);
            _incidentWidget?.UpdateState(incidentState);
            _dashboard?.UpdateIncident(incidentState);
        }

        if (_trackInfoWidget is not null || _dashboard is not null)
        {
            var trackInfoState = TrackInfoBuilder.Build(telemetry, session);
            _trackInfoWidget?.UpdateState(trackInfoState);
            _dashboard?.UpdateTrackInfo(trackInfoState);
        }

        if (_trackMapWidget is not null || _dashboard is not null)
        {
            var trackMapMarkers = TrackMapBuilder.Build(telemetry, session);
            _trackMapWidget?.UpdateState(trackMapMarkers);
            _dashboard?.UpdateTrackMap(trackMapMarkers);
        }

        // Memory usage barely changes tick to tick — reuse the same once-a-second cadence as
        // Standings rather than recomputing it on every 100ms tick.
        if (_tickCount % StandingsUpdateEveryNTicks == 0)
        {
            UpdateMemoryText();
        }
    }

    /// <summary>Hides (or reveals) every enabled widget whose "hide outside car" checkbox is on,
    /// based on whether the player is currently actually driving — without touching the "enabled"
    /// checkbox itself, so the widget picks back up exactly where it was the moment the player gets
    /// back in the car.</summary>
    private void ApplyAutoHideVisibility(bool notDriving)
    {
        ApplyAutoHideVisibilityFor(_relativeWidget, RelativeCheckBox, RelativeAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_standingsWidget, StandingsCheckBox, StandingsAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_cockpitWidget, CockpitCheckBox, CockpitAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_flagWidget, FlagCheckBox, FlagAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_tireInfoWidget, TireInfoCheckBox, TireInfoAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_deltaWidget, DeltaCheckBox, DeltaAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_fuelWidget, FuelCheckBox, FuelAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_pedalTraceWidget, PedalTraceCheckBox, PedalTraceAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_incidentWidget, IncidentCheckBox, IncidentAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_trackInfoWidget, TrackInfoCheckBox, TrackInfoAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_trackMapWidget, TrackMapCheckBox, TrackMapAutoHideCheckBox, notDriving);
        ApplyAutoHideVisibilityFor(_fuelCalculatorWidget, FuelCalculatorCheckBox, FuelCalculatorAutoHideCheckBox, notDriving);
    }

    // Skipped entirely while a widget is in edit mode — fighting the user's own Show/Hide while
    // they're actively dragging/resizing it would be actively annoying, and "hide outside car" is a
    // driving-time convenience, not something anyone needs while laying out widgets from the menu.
    private static void ApplyAutoHideVisibilityFor(
        OverlayWindowBase? widget,
        System.Windows.Controls.CheckBox enabledCheckBox,
        System.Windows.Controls.CheckBox autoHideCheckBox,
        bool notDriving)
    {
        if (widget is null || enabledCheckBox.IsChecked != true || widget.IsEditMode)
        {
            return;
        }

        var shouldHide = autoHideCheckBox.IsChecked == true && notDriving;
        if (shouldHide && widget.IsVisible)
        {
            widget.Hide();
        }
        else if (!shouldHide && !widget.IsVisible)
        {
            widget.Show();
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

        if (_cockpitWidget is not null || _dashboard is not null)
        {
            var cockpitState = CockpitBuilder.Build(telemetry, _connection.Session);
            _cockpitWidget?.UpdateState(cockpitState);
            _dashboard?.UpdateCockpit(cockpitState);
        }

        if (_pedalTraceWidget is not null || _dashboard is not null)
        {
            var pedalTraceState = _pedalTraceBuilder.Build(telemetry, _criticalTimer.Interval.TotalMilliseconds);
            _pedalTraceWidget?.UpdateState(pedalTraceState);
            _dashboard?.UpdatePedalTrace(pedalTraceState);
        }
    }

    private void UpdateDebugText(TelemetrySnapshot telemetry)
    {
        var speed = telemetry.HasVariable("Speed") ? telemetry.GetFloat("Speed") * 3.6 : 0; // m/s -> km/h
        var lap = telemetry.HasVariable("Lap") ? telemetry.GetInt("Lap") : 0;
        var gear = telemetry.HasVariable("Gear") ? telemetry.GetInt("Gear") : 0;
        DebugText.Text = $"Speed: {speed:0} km/h   Lap: {lap}   Gear: {gear}";
    }

    private void UpdateMemoryText()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var megabytes = process.WorkingSet64 / (1024.0 * 1024.0);
        var uiAvg = _uiTickSamples > 0 ? _uiTickTotalMs / _uiTickSamples : 0;
        var criticalAvg = _criticalTickSamples > 0 ? _criticalTickTotalMs / _criticalTickSamples : 0;
        var criticalTargetMs = _criticalTimer.Interval.TotalMilliseconds;

        MemoryText.Text =
            $"Memory: {megabytes:0} MB | GC0/1/2: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}\n" +
            $"UI tick: avg {uiAvg:0.0}ms max {_uiTickMaxMs:0.0}ms | " +
            $"Critical tick (target {criticalTargetMs:0}ms): avg {criticalAvg:0.0}ms max {_criticalTickMaxMs:0.0}ms, " +
            $"actual gap max {_criticalTickMaxGapMs:0}ms";

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

    private void CriticalRefreshComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var ms = CriticalRefreshComboBox.SelectedIndex switch
        {
            0 => 16,
            1 => 33,
            3 => 100,
            4 => 200,
            _ => 67, // index 2, "Normal (15 Hz)"
        };
        _criticalTimer.Interval = TimeSpan.FromMilliseconds(ms);
        CriticalRefreshStore.Save(CriticalRefreshComboBox.SelectedIndex);
    }

    /// <summary>Shared by every widget's "hide outside car" checkbox in MainWindow.xaml — each one
    /// carries its widget's store key in its Tag, since the save logic is otherwise identical for all
    /// of them. Actually hiding/showing the widget happens continuously in ApplyAutoHideVisibility,
    /// driven by live IsOnTrack telemetry, not from this handler.</summary>
    private void AutoHideCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { Tag: string widgetName } checkBox)
        {
            HideOutsideCarStore.Save(widgetName, checkBox.IsChecked == true);
        }
    }

    private void FuelCalculatorCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("FuelCalculator", FuelCalculatorCheckBox.IsChecked == true);
        if (FuelCalculatorCheckBox.IsChecked == true)
        {
            if (_fuelCalculatorWidget is null)
            {
                _fuelCalculatorWidget = new FuelCalculatorWidget();
                _fuelCalculatorWidget.SetOptions(_fuelCalculatorOptions);
            }

            _fuelCalculatorWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _fuelCalculatorWidget.Show();
        }
        else
        {
            _fuelCalculatorWidget?.Hide();
        }
    }

    /// <summary>Shared by every Fuel Calculator section checkbox — each carries the matching
    /// FuelCalculatorOptions property name in its Tag, since the panel binds its section
    /// visibilities straight to that object and the persist step is identical for all of them.</summary>
    private void FuelCalcOption_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { Tag: string optionName } checkBox)
        {
            return;
        }

        var isChecked = checkBox.IsChecked == true;
        switch (optionName)
        {
            case nameof(FuelCalculatorOptions.ShowFuelBar): _fuelCalculatorOptions.ShowFuelBar = isChecked; break;
            case nameof(FuelCalculatorOptions.ShowFuelRemaining): _fuelCalculatorOptions.ShowFuelRemaining = isChecked; break;
            case nameof(FuelCalculatorOptions.ShowLastLap): _fuelCalculatorOptions.ShowLastLap = isChecked; break;
            case nameof(FuelCalculatorOptions.ShowAverage): _fuelCalculatorOptions.ShowAverage = isChecked; break;
            case nameof(FuelCalculatorOptions.ShowMinimum): _fuelCalculatorOptions.ShowMinimum = isChecked; break;
            case nameof(FuelCalculatorOptions.ShowMaximum): _fuelCalculatorOptions.ShowMaximum = isChecked; break;
            case nameof(FuelCalculatorOptions.ShowLapsRemaining): _fuelCalculatorOptions.ShowLapsRemaining = isChecked; break;
            case nameof(FuelCalculatorOptions.ShowFuelToFinish): _fuelCalculatorOptions.ShowFuelToFinish = isChecked; break;
            case nameof(FuelCalculatorOptions.ShowRefuel): _fuelCalculatorOptions.ShowRefuel = isChecked; break;
        }

        FuelCalculatorOptionsStore.Save(_fuelCalculatorOptions);
    }

    private void FuelAverageSourceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _fuelCalculatorOptions.AverageSource = (FuelAverageSource)Math.Max(0, FuelAverageSourceComboBox.SelectedIndex);
        FuelCalculatorOptionsStore.Save(_fuelCalculatorOptions);
    }

    // Unparseable or negative input is ignored rather than reset to 0 — the user is mid-typing (an
    // empty box, or just "1." on the way to "1.5") and blanking their entry under them is hostile.
    private void FuelMargin_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (double.TryParse(FuelMarginLapsTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var laps) && laps >= 0)
        {
            _fuelCalculatorOptions.MarginLaps = laps;
        }

        if (double.TryParse(FuelMarginLitersTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var liters) && liters >= 0)
        {
            _fuelCalculatorOptions.MarginLiters = liters;
        }

        FuelCalculatorOptionsStore.Save(_fuelCalculatorOptions);
    }

    private void RelativeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("Relative", RelativeCheckBox.IsChecked == true);
        if (RelativeCheckBox.IsChecked == true)
        {
            _relativeWidget ??= new RelativeWidget();
            _relativeWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _relativeWidget.Show();
        }
        else
        {
            _relativeWidget?.Hide();
        }
    }

    private void StandingsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("Standings", StandingsCheckBox.IsChecked == true);
        if (StandingsCheckBox.IsChecked == true)
        {
            if (_standingsWidget is null)
            {
                _standingsWidget = new StandingsWidget();
                _standingsWidget.SetColumnVisibility(_standingsColumnVisibility);
            }

            _standingsWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _standingsWidget.Show();
        }
        else
        {
            _standingsWidget?.Hide();
        }
    }

    private void CockpitCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("Cockpit", CockpitCheckBox.IsChecked == true);
        if (CockpitCheckBox.IsChecked == true)
        {
            _cockpitWidget ??= new CockpitWidget();
            _cockpitWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _cockpitWidget.Show();
        }
        else
        {
            _cockpitWidget?.Hide();
        }
    }

    private void FlagCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("Flag", FlagCheckBox.IsChecked == true);
        if (FlagCheckBox.IsChecked == true)
        {
            _flagWidget ??= new FlagWidget();
            _flagWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _flagWidget.Show();
        }
        else
        {
            _flagWidget?.Hide();
        }
    }

    private void TireInfoCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("TireInfo", TireInfoCheckBox.IsChecked == true);
        if (TireInfoCheckBox.IsChecked == true)
        {
            _tireInfoWidget ??= new TireInfoWidget();
            _tireInfoWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _tireInfoWidget.Show();
        }
        else
        {
            _tireInfoWidget?.Hide();
        }
    }

    private void DeltaCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("Delta", DeltaCheckBox.IsChecked == true);
        if (DeltaCheckBox.IsChecked == true)
        {
            _deltaWidget ??= new DeltaWidget();
            _deltaWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _deltaWidget.Show();
        }
        else
        {
            _deltaWidget?.Hide();
        }
    }

    private void FuelCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("Fuel", FuelCheckBox.IsChecked == true);
        if (FuelCheckBox.IsChecked == true)
        {
            _fuelWidget ??= new FuelWidget();
            _fuelWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _fuelWidget.Show();
        }
        else
        {
            _fuelWidget?.Hide();
        }
    }

    private void PedalTraceCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("PedalTrace", PedalTraceCheckBox.IsChecked == true);
        if (PedalTraceCheckBox.IsChecked == true)
        {
            _pedalTraceWidget ??= new PedalTraceWidget();
            _pedalTraceWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _pedalTraceWidget.Show();
        }
        else
        {
            _pedalTraceWidget?.Hide();
        }
    }

    private void IncidentCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("Incident", IncidentCheckBox.IsChecked == true);
        if (IncidentCheckBox.IsChecked == true)
        {
            _incidentWidget ??= new IncidentWidget();
            _incidentWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _incidentWidget.Show();
        }
        else
        {
            _incidentWidget?.Hide();
        }
    }

    private void TrackInfoCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("TrackInfo", TrackInfoCheckBox.IsChecked == true);
        if (TrackInfoCheckBox.IsChecked == true)
        {
            _trackInfoWidget ??= new TrackInfoWidget();
            _trackInfoWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _trackInfoWidget.Show();
        }
        else
        {
            _trackInfoWidget?.Hide();
        }
    }

    private void TrackMapCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("TrackMap", TrackMapCheckBox.IsChecked == true);
        if (TrackMapCheckBox.IsChecked == true)
        {
            _trackMapWidget ??= new TrackMapWidget();
            _trackMapWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            _trackMapWidget.Show();
        }
        else
        {
            _trackMapWidget?.Hide();
        }
    }

    private void StandingsIRatingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _standingsColumnVisibility.ShowIRating = StandingsIRatingCheckBox.IsChecked == true;
        StandingsColumnVisibilityStore.Save(nameof(StandingsColumnVisibility.ShowIRating), _standingsColumnVisibility.ShowIRating);
    }

    private void StandingsIRatingDeltaCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _standingsColumnVisibility.ShowIRatingDelta = StandingsIRatingDeltaCheckBox.IsChecked == true;
        StandingsColumnVisibilityStore.Save(nameof(StandingsColumnVisibility.ShowIRatingDelta), _standingsColumnVisibility.ShowIRatingDelta);
    }

    private void StandingsLicenseCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _standingsColumnVisibility.ShowLicense = StandingsLicenseCheckBox.IsChecked == true;
        StandingsColumnVisibilityStore.Save(nameof(StandingsColumnVisibility.ShowLicense), _standingsColumnVisibility.ShowLicense);
    }

    private void StandingsLapCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _standingsColumnVisibility.ShowLap = StandingsLapCheckBox.IsChecked == true;
        StandingsColumnVisibilityStore.Save(nameof(StandingsColumnVisibility.ShowLap), _standingsColumnVisibility.ShowLap);
    }

    private void StandingsLastLapCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _standingsColumnVisibility.ShowLastLap = StandingsLastLapCheckBox.IsChecked == true;
        StandingsColumnVisibilityStore.Save(nameof(StandingsColumnVisibility.ShowLastLap), _standingsColumnVisibility.ShowLastLap);
    }

    private void StandingsBestLapCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _standingsColumnVisibility.ShowBestLap = StandingsBestLapCheckBox.IsChecked == true;
        StandingsColumnVisibilityStore.Save(nameof(StandingsColumnVisibility.ShowBestLap), _standingsColumnVisibility.ShowBestLap);
    }

    private void StandingsGapCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _standingsColumnVisibility.ShowGap = StandingsGapCheckBox.IsChecked == true;
        StandingsColumnVisibilityStore.Save(nameof(StandingsColumnVisibility.ShowGap), _standingsColumnVisibility.ShowGap);
    }

    private void DeltaReferenceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _deltaReference = DeltaReferenceComboBox.SelectedIndex switch
        {
            1 => DeltaReference.PersonalBestAllTime,
            2 => DeltaReference.OptimalLap,
            _ => DeltaReference.SessionBest,
        };
    }

    private void DashboardThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var theme = DashboardThemeComboBox.SelectedIndex switch
        {
            1 => DashboardTheme.DigitalHud,
            2 => DashboardTheme.RawDiy,
            _ => DashboardTheme.Classic,
        };
        DashboardThemeStore.Save(theme);
        _dashboard?.ApplyTheme(theme);
    }

    private void EditModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var editMode = EditModeCheckBox.IsChecked == true;
        if (_relativeWidget is not null)
        {
            _relativeWidget.IsEditMode = editMode;
        }

        if (_standingsWidget is not null)
        {
            _standingsWidget.IsEditMode = editMode;
        }

        if (_cockpitWidget is not null)
        {
            _cockpitWidget.IsEditMode = editMode;
        }

        if (_flagWidget is not null)
        {
            _flagWidget.IsEditMode = editMode;
        }

        if (_tireInfoWidget is not null)
        {
            _tireInfoWidget.IsEditMode = editMode;
        }

        if (_deltaWidget is not null)
        {
            _deltaWidget.IsEditMode = editMode;
        }

        if (_fuelWidget is not null)
        {
            _fuelWidget.IsEditMode = editMode;
        }

        if (_pedalTraceWidget is not null)
        {
            _pedalTraceWidget.IsEditMode = editMode;
        }

        if (_incidentWidget is not null)
        {
            _incidentWidget.IsEditMode = editMode;
        }

        if (_trackInfoWidget is not null)
        {
            _trackInfoWidget.IsEditMode = editMode;
        }

        if (_trackMapWidget is not null)
        {
            _trackMapWidget.IsEditMode = editMode;
        }

        if (_fuelCalculatorWidget is not null)
        {
            _fuelCalculatorWidget.IsEditMode = editMode;
        }
    }

    private void ShowDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dashboard is { IsVisible: true })
        {
            _dashboard.Hide();
            ShowDashboardButton.Content = "Show dashboard";
            return;
        }

        if (MonitorComboBox.SelectedItem is not Screen screen)
        {
            return;
        }

        if (_dashboard is null)
        {
            _dashboard = new DashboardWindow();
            _dashboard.Closed += (_, _) => ShowDashboardButton.Content = "Show dashboard";
        }

        _dashboard.MoveToScreen(screen);
        ShowDashboardButton.Content = "Hide dashboard";
    }
}
