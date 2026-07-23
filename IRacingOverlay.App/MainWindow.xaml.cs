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
    private readonly SessionBestLapTracker _sessionBestLapTracker = new();
    private int _tickCount;

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
    private DashboardWindow? _dashboard;
    private DeltaReference _deltaReference = DeltaReference.SessionBest;
    private readonly StandingsColumnVisibility _standingsColumnVisibility = new();

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
            _dashboard?.Close();
        };

        RestoreWidgetVisibility();
        RestoreHideInPitCheckboxes();
        RestoreStandingsColumnVisibility();

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
    }

    /// <summary>Re-checks whichever "hide in pit" checkboxes were checked last run — each one shares
    /// the single HideInPitCheckBox_Changed handler (wired via XAML) keyed off its Tag, so setting
    /// IsChecked here is enough to both restore and persist the same value.</summary>
    private void RestoreHideInPitCheckboxes()
    {
        RelativeHideInPitCheckBox.IsChecked = HideInPitStore.Get("Relative");
        StandingsHideInPitCheckBox.IsChecked = HideInPitStore.Get("Standings");
        CockpitHideInPitCheckBox.IsChecked = HideInPitStore.Get("Cockpit");
        FlagHideInPitCheckBox.IsChecked = HideInPitStore.Get("Flag");
        TireInfoHideInPitCheckBox.IsChecked = HideInPitStore.Get("TireInfo");
        DeltaHideInPitCheckBox.IsChecked = HideInPitStore.Get("Delta");
        FuelHideInPitCheckBox.IsChecked = HideInPitStore.Get("Fuel");
        PedalTraceHideInPitCheckBox.IsChecked = HideInPitStore.Get("PedalTrace");
        IncidentHideInPitCheckBox.IsChecked = HideInPitStore.Get("Incident");
        TrackInfoHideInPitCheckBox.IsChecked = HideInPitStore.Get("TrackInfo");
        TrackMapHideInPitCheckBox.IsChecked = HideInPitStore.Get("TrackMap");
    }

    private void SetStatus(bool connected)
    {
        StatusDot.Fill = connected ? Brushes.LimeGreen : Brushes.Red;
        StatusText.Text = connected ? "Connected to iRacing" : "Waiting for iRacing…";
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        var telemetry = _connection.Latest;
        if (telemetry is null)
        {
            return;
        }

        UpdateDebugText(telemetry);

        var playerOnPitRoad = telemetry.HasVariable(TelemetryVarNames.OnPitRoad) && telemetry.GetBool(TelemetryVarNames.OnPitRoad);
        ApplyPitVisibility(playerOnPitRoad);

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

    /// <summary>Hides (or reveals) every enabled widget whose "hide in pit" checkbox is on, based on
    /// the player's own pit-road status — without touching the "enabled" checkbox itself, so the
    /// widget picks back up exactly where it was the moment the player leaves the pits.</summary>
    private void ApplyPitVisibility(bool onPitRoad)
    {
        ApplyPitVisibilityFor(_relativeWidget, RelativeCheckBox, RelativeHideInPitCheckBox, onPitRoad);
        ApplyPitVisibilityFor(_standingsWidget, StandingsCheckBox, StandingsHideInPitCheckBox, onPitRoad);
        ApplyPitVisibilityFor(_cockpitWidget, CockpitCheckBox, CockpitHideInPitCheckBox, onPitRoad);
        ApplyPitVisibilityFor(_flagWidget, FlagCheckBox, FlagHideInPitCheckBox, onPitRoad);
        ApplyPitVisibilityFor(_tireInfoWidget, TireInfoCheckBox, TireInfoHideInPitCheckBox, onPitRoad);
        ApplyPitVisibilityFor(_deltaWidget, DeltaCheckBox, DeltaHideInPitCheckBox, onPitRoad);
        ApplyPitVisibilityFor(_fuelWidget, FuelCheckBox, FuelHideInPitCheckBox, onPitRoad);
        ApplyPitVisibilityFor(_pedalTraceWidget, PedalTraceCheckBox, PedalTraceHideInPitCheckBox, onPitRoad);
        ApplyPitVisibilityFor(_incidentWidget, IncidentCheckBox, IncidentHideInPitCheckBox, onPitRoad);
        ApplyPitVisibilityFor(_trackInfoWidget, TrackInfoCheckBox, TrackInfoHideInPitCheckBox, onPitRoad);
        ApplyPitVisibilityFor(_trackMapWidget, TrackMapCheckBox, TrackMapHideInPitCheckBox, onPitRoad);
    }

    // Skipped entirely while a widget is in edit mode — fighting the user's own Show/Hide while
    // they're actively dragging/resizing it would be actively annoying, and "hide in pit" is a
    // driving-time convenience, not something anyone needs while parked in the garage laying out
    // widgets.
    private static void ApplyPitVisibilityFor(
        OverlayWindowBase? widget,
        System.Windows.Controls.CheckBox enabledCheckBox,
        System.Windows.Controls.CheckBox hideInPitCheckBox,
        bool onPitRoad)
    {
        if (widget is null || enabledCheckBox.IsChecked != true || widget.IsEditMode)
        {
            return;
        }

        var shouldHide = hideInPitCheckBox.IsChecked == true && onPitRoad;
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
            var pedalTraceState = _pedalTraceBuilder.Build(telemetry);
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
        var megabytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        MemoryText.Text = $"Memory: {megabytes:0} MB";
    }

    private void CriticalRefreshComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var ms = CriticalRefreshComboBox.SelectedIndex switch
        {
            0 => 33,
            1 => 50,
            3 => 200,
            4 => 500,
            _ => 100, // index 2, "Normal (10 Hz)"
        };
        _criticalTimer.Interval = TimeSpan.FromMilliseconds(ms);
        CriticalRefreshStore.Save(CriticalRefreshComboBox.SelectedIndex);
    }

    /// <summary>Shared by every widget's "hide in pit" checkbox in MainWindow.xaml — each one carries
    /// its widget's store key in its Tag, since the save logic is otherwise identical for all of
    /// them. Actually hiding/showing the widget happens continuously in ApplyPitVisibility, driven by
    /// live pit-road telemetry, not from this handler.</summary>
    private void HideInPitCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { Tag: string widgetName } checkBox)
        {
            HideInPitStore.Save(widgetName, checkBox.IsChecked == true);
        }
    }

    private void RelativeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        WidgetVisibilityStore.Save("Relative", RelativeCheckBox.IsChecked == true);
        if (RelativeCheckBox.IsChecked == true)
        {
            _relativeWidget ??= new RelativeWidget();
            // A brand-new widget (no saved position yet) starts in edit mode so it can be placed;
            // only force it from the checkbox once it actually has a position worth locking.
            if (_relativeWidget.HasSavedLayout)
            {
                _relativeWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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

            if (_standingsWidget.HasSavedLayout)
            {
                _standingsWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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
            if (_cockpitWidget.HasSavedLayout)
            {
                _cockpitWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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
            if (_flagWidget.HasSavedLayout)
            {
                _flagWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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
            if (_tireInfoWidget.HasSavedLayout)
            {
                _tireInfoWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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
            if (_deltaWidget.HasSavedLayout)
            {
                _deltaWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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
            if (_fuelWidget.HasSavedLayout)
            {
                _fuelWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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
            if (_pedalTraceWidget.HasSavedLayout)
            {
                _pedalTraceWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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
            if (_incidentWidget.HasSavedLayout)
            {
                _incidentWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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
            if (_trackInfoWidget.HasSavedLayout)
            {
                _trackInfoWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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
            if (_trackMapWidget.HasSavedLayout)
            {
                _trackMapWidget.IsEditMode = EditModeCheckBox.IsChecked == true;
            }

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
