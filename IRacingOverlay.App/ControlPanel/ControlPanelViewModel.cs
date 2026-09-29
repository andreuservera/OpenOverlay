using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;
using IRacingOverlay.Sdk;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// The control panel's state, entire. The window is a rendering of this and nothing else — there is
/// no control the code-behind reads back out, which is what the old panel did through two dozen
/// named checkboxes and is why restoring settings there had to fight the order XAML happened to
/// construct things in.
///
/// The one thing this class deliberately does not own is telemetry: MainWindow keeps the connection
/// and the two update loops, and pushes status and diagnostics in. The split is "what the user has
/// chosen" versus "what the car is doing", and it keeps the configuration testable without a sim.
/// </summary>
public sealed partial class ControlPanelViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<string, WidgetSlot> _slots = [];

    private NavItem? _selected;
    private bool _isEditMode;
    private bool _isConnected;
    private ConnectionState _connectionState;
    private HealthStatus _health;
    private string _healthLine = "Starting";
    private string _telemetryLine = "Speed —   Lap —   Gear —";
    private string _diagnosticsLine = "";
    private string _searchText = "";
    private int _selectedMonitorIndex;
    private DashboardTheme _dashboardTheme;
    private DeltaReference _deltaReference = DeltaReference.SessionBest;
    private int _criticalRefreshIndex;

    public ControlPanelViewModel()
    {
        // Loaded before anything can look at them, so every setting item below is constructed
        // already holding the value from the last run. Nothing in the UI can write a default back
        // over these, because no control in this window has a XAML-declared value to begin with.
        DriverTableOptionsStore.ApplyTo(StandingsOptions);
        DriverTableOptionsStore.ApplyTo(RelativeOptions);
        FuelCalculatorOptionsStore.ApplyTo(FuelCalculatorOptions);
        FlagOptionsStore.ApplyTo(FlagOptions);
        WeatherOptionsStore.ApplyTo(WeatherOptions);
        CockpitOptions.Theme = CockpitThemeStore.Get();
        _dashboardTheme = DashboardThemeStore.Get();
        _criticalRefreshIndex = CriticalRefreshStore.Get();
        Units.SetPreference(UnitPreferenceStore.Get());

        // Settings with a unit (the fuel reserve) are built in the sim's units, so rebuild on a switch.
        Units.CurrentChanged += () =>
        {
            if (Selected is { } selected)
            {
                BuildSettings(selected);
            }
        };

        foreach (var descriptor in WidgetCatalog.All)
        {
            var slot = new WidgetSlot(descriptor, () => CreateWidget(descriptor.Key));
            _slots[descriptor.Key] = slot;
            NavItems.Add(NavItem.ForWidget(slot));
        }

        NavItems.Add(NavItem.ForPage(
            GeneralPageKey, "General", "How the app runs: closing, the tray and where settings live.",
            "M12,9 A3,3 0 1 0 12.01,9 Z M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M4.9,19.1 L7,17 M17,7 L19.1,4.9"));

        NavItems.Add(NavItem.ForPage(
            DashboardPageKey, "Dashboard", "The fullscreen layout for a second monitor.",
            "M3,4 H21 V16 H3 Z M9,20 H15 M12,16 V20"));

        NavItems.Add(NavItem.ForPage(
            PerformancePageKey, "Performance", "How hard the overlay works for the displays that need it.",
            "M4,20 A9,9 0 1 1 20,20 M12,14 L16,9"));

        NavItems.Add(NavItem.ForPage(
            UnitsPageKey, "Units", "Metric or imperial, for every overlay at once.",
            "M3,17 L17,3 L21,7 L7,21 Z M7,13 L9,15 M10,10 L12,12 M13,7 L15,9"));

        NavItems.Add(NavItem.ForPage(
            HotkeysPageKey, "Hotkeys", "Control the overlay from inside iRacing, without Alt+Tab.",
            "M3,6 H21 V18 H3 Z M6,9 H7 M9.5,9 H10.5 M13,9 H14 M16.5,9 H17.5 M6,12 H7 M17,12 H18 M8,15 H16"));

        MonitorNames = Screen.AllScreens.Select(DescribeScreen).ToList();
        // Second monitor by default: a dashboard on the same screen as the sim is in the way, which
        // is the one thing it must never be.
        _selectedMonitorIndex = MonitorNames.Count > 1 ? 1 : 0;

        NavView = CollectionViewSource.GetDefaultView(NavItems);
        NavView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavItem.Group)));

        Select(NavItems[0]);
    }

    private const string DashboardPageKey = "app.dashboard";
    private const string GeneralPageKey = "app.general";
    private const string PerformancePageKey = "app.performance";
    private const string UnitsPageKey = "app.units";

    // ===== Shared option objects =====
    // These are the same instances handed to the widgets and to the preview, which is what makes a
    // toggle reach both without a notification path in between.
    public DriverTableOptions StandingsOptions { get; } = new(DriverTable.Standings);
    public DriverTableOptions RelativeOptions { get; } = new(DriverTable.Relative);
    public FuelCalculatorOptions FuelCalculatorOptions { get; } = new();
    public FlagOptions FlagOptions { get; } = new();
    public CockpitOptions CockpitOptions { get; } = new();
    public WeatherOptions WeatherOptions { get; } = new();

    /// <summary>Which flag the preview is simulating. Preview-only state: never persisted, never
    /// seen by the live widget.</summary>
    public FlagPreviewScenario FlagPreview { get; } = new();

    public ObservableCollection<NavItem> NavItems { get; } = [];

    public ICollectionView NavView { get; }

    public IReadOnlyCollection<WidgetSlot> Slots => _slots.Values;

    public IReadOnlyList<string> MonitorNames { get; }

    public WidgetSlot SlotOf(string key) => _slots[key];

    /// <summary>Typed access to a widget window for the telemetry loop, without every caller having
    /// to know whether it has been created yet.</summary>
    public T? WidgetOf<T>(string key) where T : OverlayWindowBase => _slots[key].WindowAs<T>();

    public NavItem? Selected
    {
        get => _selected;
        set => Select(value);
    }

    /// <summary>Filters the rail. Matching on the blurb as well as the name is deliberate: it lets
    /// someone find the widget by what it does ("fuel", "gap", "weather") when they don't yet know
    /// what this application decided to call it.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value;
            var term = value.Trim();
            NavView.Filter = term.Length == 0
                ? null
                : item => item is NavItem nav &&
                          (nav.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                           nav.Blurb.Contains(term, StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged();
        }
    }

    /// <summary>Layout-editing mode. A single switch for the whole application rather than per
    /// widget: "let me move things" is a mode you are in, not a property of one panel.</summary>
    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (_isEditMode == value)
            {
                return;
            }

            _isEditMode = value;
            foreach (var slot in _slots.Values)
            {
                slot.IsEditMode = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(EditModeLabel));
        }
    }

    public string EditModeLabel => _isEditMode ? "Editing layout" : "Edit layout";

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionLabel));
        }
    }

    public string ConnectionLabel => _connectionState switch
    {
        ConnectionState.Stale => "TELEMETRY STALLED",
        ConnectionState.Recovering => "RECONNECTING",
        _ => _isConnected ? "CONNECTED" : "WAITING FOR IRACING",
    };

    /// <summary>The reader's own view of the link, polled once a second: it knows about stalls and
    /// retries that the connected/disconnected events never report.</summary>
    public ConnectionState ConnectionState
    {
        get => _connectionState;
        set
        {
            if (_connectionState == value)
            {
                return;
            }

            _connectionState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionLabel));
            OnPropertyChanged(nameof(ConnectionDegraded));
        }
    }

    public bool ConnectionDegraded => _connectionState is ConnectionState.Stale or ConnectionState.Recovering;

    public HealthStatus Health
    {
        get => _health;
        set
        {
            if (_health == value)
            {
                return;
            }

            _health = value;
            OnPropertyChanged();
        }
    }

    public string HealthLine
    {
        get => _healthLine;
        set
        {
            if (_healthLine == value)
            {
                return;
            }

            _healthLine = value;
            OnPropertyChanged();
        }
    }

    public string TelemetryLine
    {
        get => _telemetryLine;
        set
        {
            _telemetryLine = value;
            OnPropertyChanged();
        }
    }

    public string DiagnosticsLine
    {
        get => _diagnosticsLine;
        set
        {
            _diagnosticsLine = value;
            OnPropertyChanged();
        }
    }

    public int SelectedMonitorIndex => _selectedMonitorIndex;

    public DeltaReference DeltaReference => _deltaReference;

    /// <summary>Timer period for the two displays whose whole value is latency — the proximity/ABS
    /// bars and the pedal trace. Index order matches the labels the Performance page offers.</summary>
    public int CriticalRefreshIntervalMs => _criticalRefreshIndex switch
    {
        0 => 16,
        1 => 33,
        3 => 100,
        4 => 200,
        _ => 67,
    };

    /// <summary>Raised when the dashboard theme changes, so the window can repaint if it's open.
    /// The choice itself is already persisted by the time this fires.</summary>
    public event Action<DashboardTheme>? DashboardThemeChanged;

    /// <summary>Raised when the critical refresh rate changes, carrying the new period in ms.</summary>
    public event Action<int>? CriticalRefreshChanged;

    /// <summary>Raised when the user asks to show or hide the dashboard.</summary>
    public event Action? DashboardToggleRequested;

    /// <summary>Raised when a header field of a driver table is switched on or off. Those are pushed
    /// on the standings tick, so the affected widget needs telling to clear the value now rather
    /// than leaving a stale one on screen for up to a second.</summary>
    public event Action<DriverTable>? TableHeaderChanged;

    /// <summary>Caption on the dashboard page's button, flipped by MainWindow as the window opens
    /// and closes so the button always names what it will do next.</summary>
    public ActionSetting? DashboardButton { get; private set; }

    /// <summary>Brings every widget on screen that was on last time. Called once the window is up so
    /// the overlays appear over iRacing rather than behind a control panel still being laid out.</summary>
    public void RestoreVisibleWidgets()
    {
        foreach (var slot in _slots.Values)
        {
            slot.IsEditMode = _isEditMode;
            slot.Apply();
        }
    }

    public void CloseAllWidgets()
    {
        foreach (var slot in _slots.Values)
        {
            slot.Close();
        }
    }

    private void Select(NavItem? item)
    {
        // A null arrives when the search filters the selected entry out from under the list. Keeping
        // the current page is the useful behaviour: the pane the user was working in stays put while
        // they look for the next one, instead of blanking on every keystroke.
        if (item is null || ReferenceEquals(_selected, item))
        {
            return;
        }

        _selected = item;

        // The widget's own +/- control writes the same setting, so the page is rebuilt from current
        // state on every visit rather than trusting what it showed last time.
        item.Widget?.RefreshScaleFromWidget();
        BuildSettings(item);

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(SelectedWidget));
        OnPropertyChanged(nameof(HasPreview));
    }

    public WidgetSlot? SelectedWidget => _selected?.Widget;

    public bool HasPreview => _selected?.IsWidget == true;

    private OverlayWindowBase CreateWidget(string key) => key switch
    {
        WidgetCatalog.Relative => Configured(new RelativeWidget(), w => w.SetOptions(RelativeOptions)),
        WidgetCatalog.Standings => Configured(new StandingsWidget(), w => w.SetOptions(StandingsOptions)),
        WidgetCatalog.Cockpit => Configured(new CockpitWidget(), w => w.SetOptions(CockpitOptions)),
        WidgetCatalog.Flag => Configured(new FlagWidget(), w => w.SetOptions(FlagOptions)),
        WidgetCatalog.TireInfo => new TireInfoWidget(),
        WidgetCatalog.Delta => new DeltaWidget(),
        WidgetCatalog.Fuel => new FuelWidget(),
        WidgetCatalog.PedalTrace => new PedalTraceWidget(),
        WidgetCatalog.Incident => new IncidentWidget(),
        WidgetCatalog.TrackInfo => new TrackInfoWidget(),
        WidgetCatalog.Weather => Configured(new WeatherWidget(), w => w.SetOptions(WeatherOptions)),
        WidgetCatalog.TrackMap => new TrackMapWidget(),
        WidgetCatalog.FuelCalculator => Configured(new FuelCalculatorWidget(), w => w.SetOptions(FuelCalculatorOptions)),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No factory registered for this widget."),
    };

    private static T Configured<T>(T widget, Action<T> configure) where T : OverlayWindowBase
    {
        configure(widget);
        return widget;
    }

    private static string DescribeScreen(Screen screen, int index)
    {
        var bounds = screen.Bounds;
        var role = screen.Primary ? "primary" : $"display {index + 1}";
        return $"{bounds.Width}×{bounds.Height} ({role})";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
