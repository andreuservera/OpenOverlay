using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Layouts;
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
    private string? _sessionLabel;
    private HealthStatus _health;
    private string _healthLine = "Starting";
    private string _telemetryLine = Loc.T("status.telemetry.none");
    private string _diagnosticsLine = "";
    private int _selectedMonitorIndex;
    private DashboardTheme _dashboardTheme;
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
        TrackInfoOptionsStore.ApplyTo(TrackInfoOptions);
        PedalTraceOptionsStore.ApplyTo(PedalTraceOptions);
        CockpitOptionsStore.ApplyTo(CockpitOptions);
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

        // A new language rewrites everything the window says: the bound texts refresh by
        // themselves, the page in view is rebuilt and search starts over in the new words.
        Loc.Changed += () =>
        {
            _searchIndex = null;
            if (Selected is { } selected)
            {
                BuildSettings(selected);
            }

            UpdateWidgetCount();
            OnPropertyChanged(string.Empty);
            // A dropdown handed its list in the new language drops its selection; once every list
            // has been swapped, say the selections again so each picks its entry back up.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                () => OnPropertyChanged(string.Empty), System.Windows.Threading.DispatcherPriority.Background);
        };

        AddHomePage();

        foreach (var descriptor in WidgetCatalog.All)
        {
            var slot = new WidgetSlot(descriptor, () => CreateWidget(descriptor.Key));
            slot.CameOnScreen += OnWidgetCameOnScreen;
            slot.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WidgetSlot.IsEnabled))
                {
                    UpdateWidgetCount();
                }
                else if (e.PropertyName == nameof(WidgetSlot.State))
                {
                    RefreshHome();
                }
            };
            _slots[descriptor.Key] = slot;
            NavItems.Add(NavItem.ForWidget(slot));
        }

        // Layouts stands alone, without a heading: one page, and the one most people open next.
        NavItems.Add(NavItem.ForPage(
            LayoutsPageKey, "nav.layouts.title", "nav.layouts.blurb",
            "M3,4 H21 V20 H3 Z M3,9 H21 M10,9 V20", "#layouts", isPinned: true));

        NavItems.Add(NavItem.ForPage(
            GeneralPageKey, "nav.general.title", "nav.general.blurb",
            "M9.65,4.77 L10.26,2.15 L13.74,2.15 L14.35,4.77 L15.45,5.23 L17.74,3.81 L20.19,6.26 L18.77,8.55 L19.23,9.65 L21.85,10.26 L21.85,13.74 L19.23,14.35 L18.77,15.45 L20.19,17.74 L17.74,20.19 L15.45,18.77 L14.35,19.23 L13.74,21.85 L10.26,21.85 L9.65,19.23 L8.55,18.77 L6.26,20.19 L3.81,17.74 L5.23,15.45 L4.77,14.35 L2.15,13.74 L2.15,10.26 L4.77,9.65 L5.23,8.55 L3.81,6.26 L6.26,3.81 L8.55,5.23 Z M12,8.5 A3.5,3.5 0 1 0 12.01,8.5 Z",
            SettingsGroupTitle, isPinned: true));

        NavItems.Add(NavItem.ForPage(
            HotkeysPageKey, "nav.hotkeys.title", "nav.hotkeys.blurb",
            "M3,6 H21 V18 H3 Z M7,10 H7.01 M11,10 H11.01 M15,10 H15.01 M7,14 H17",
            SettingsGroupTitle, isPinned: true));

        NavItems.Add(NavItem.ForPage(
            DiagnosticsPageKey, "nav.diagnostics.title", "nav.diagnostics.blurb",
            "M3,12 H7 L9.5,6 L14.5,18 L17,12 H21",
            SettingsGroupTitle, isPinned: true));

        AddHelpPage();

        MonitorNames = Screen.AllScreens.Select(DescribeScreen).ToList();
        // Second monitor by default: a dashboard on the same screen as the sim is in the way, which
        // is the one thing it must never be.
        _selectedMonitorIndex = MonitorNames.Count > 1 ? 1 : 0;

        UpdateWidgetCount();

        // Two lists over the same pages, each with its own view: the widgets scroll at the top, the
        // pinned pages (Layouts, settings) stay at the foot. Help sits below both, in neither.
        NavView = new ListCollectionView(NavItems);
        NavView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavItem.Section)));
        NavView.Filter = item => item is NavItem { IsFooter: false, IsPinned: false };
        PinnedNavView = new ListCollectionView(NavItems);
        PinnedNavView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavItem.Section)));
        PinnedNavView.Filter = item => item is NavItem { IsPinned: true };

        Select(NavItems[0]);
    }

    private const string GeneralPageKey = "app.general";
    private const string HotkeysPageKey = "app.hotkeys";
    private const string DiagnosticsPageKey = "app.diagnostics";
    private const string SettingsGroupTitle = "SETTINGS";

    // ===== Shared option objects =====
    // These are the same instances handed to the widgets and to the preview, which is what makes a
    // toggle reach both without a notification path in between.
    public DriverTableOptions StandingsOptions { get; } = new(DriverTable.Standings);
    public DriverTableOptions RelativeOptions { get; } = new(DriverTable.Relative);
    public FuelCalculatorOptions FuelCalculatorOptions { get; } = new();
    public FlagOptions FlagOptions { get; } = new();
    public CockpitOptions CockpitOptions { get; } = new();
    public WeatherOptions WeatherOptions { get; } = new();

    public TrackInfoOptions TrackInfoOptions { get; } = new();
    public DeltaOptions DeltaOptions { get; } = new();
    public PedalTraceOptions PedalTraceOptions { get; } = new();

    /// <summary>Which flag the preview is simulating. Preview-only state: never persisted, never
    /// seen by the live widget.</summary>
    public FlagPreviewScenario FlagPreview { get; } = new();

    public ObservableCollection<NavItem> NavItems { get; } = [];

    public ICollectionView NavView { get; }

    public ICollectionView PinnedNavView { get; }

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

    /// <summary>The rail's selection: the open page, or nothing when the page isn't listed in the
    /// rail (Help, at its foot). Kept apart from <see cref="Selected"/> so the rail never goes on
    /// lighting the page that was open before Help, and so clicking that page opens it again.</summary>
    public NavItem? RailSelected
    {
        get => _selected is { IsFooter: false, IsPinned: false } page ? page : null;
        set => Select(value);
    }

    /// <summary>The selection of the rail's pinned list at its foot, on the same terms as
    /// <see cref="RailSelected"/>: only one of the two lists ever lights a page.</summary>
    public NavItem? PinnedRailSelected
    {
        get => _selected is { IsPinned: true } page ? page : null;
        set => Select(value);
    }

    private const string RailCollapseKey = "Rail/IconsOnly";
    private ICommand? _toggleRail;

    /// <summary>The rail folded to its icons, for more room to the settings and the preview. The
    /// names are one hover away; remembered for next time.</summary>
    public bool IsRailCollapsed
    {
        get => CollapseStore.IsCollapsed(RailCollapseKey);
        set
        {
            if (value == IsRailCollapsed)
            {
                return;
            }

            CollapseStore.SetCollapsed(RailCollapseKey, value);
            OnPropertyChanged();
        }
    }

    public ICommand ToggleRailCommand => _toggleRail ??= new RelayCommand(() => IsRailCollapsed = !IsRailCollapsed);

    /// <summary>Widgets unlocked: they can be dragged and resized on screen. A single switch for the
    /// whole application rather than per widget: "let me move things" is a mode you are in, not a
    /// property of one panel. Widgets an open layout controls move too; that is saved as their own
    /// position, never in the layout, and undone when the layout closes.</summary>
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
            AppLog.Activity("Control Panel", value ? "Widgets unlocked" : "Widgets locked");
            foreach (var slot in _slots.Values)
            {
                slot.IsEditMode = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(EditModeLabel));
            RefreshHome();
        }
    }

    /// <summary>What the toolbar button does when clicked.</summary>
    public string EditModeLabel => Loc.T(_isEditMode ? "toolbar.editMode.done" : "toolbar.editMode.move");

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
            OnConnectionChanged();
        }
    }

    /// <summary>The toolbar's status pill, in words. An application problem outranks the link,
    /// since it says the rest can't be trusted; then a link that is up but stalled; then the session
    /// itself.</summary>
    public string ConnectionLabel => ConnectionTone switch
    {
        ConnectionTone.Error => Loc.T("connection.problem"),
        ConnectionTone.Degraded => Loc.T(_connectionState == ConnectionState.Stale ? "connection.stalled" : "connection.reconnecting"),
        ConnectionTone.Live => string.IsNullOrEmpty(_sessionLabel) ? Loc.T("connection.connected") : Loc.F("connection.connectedSession", _sessionLabel),
        _ => Loc.T("connection.waiting"),
    };

    /// <summary>What to do about the state the pill shows, when there is something to do.</summary>
    public string ConnectionHint => ConnectionTone == ConnectionTone.Waiting
        ? Loc.T("connection.waitingHint")
        : "";

    /// <summary>The pill's colour: green connected, amber waiting or degraded, red a problem.</summary>
    public ConnectionTone ConnectionTone =>
        _health == HealthStatus.Failed ? ConnectionTone.Error
        : ConnectionDegraded ? ConnectionTone.Degraded
        : _isConnected ? ConnectionTone.Live
        : ConnectionTone.Waiting;

    /// <summary>Track and session for the pill ("Spa · Race"), pushed in by the window while
    /// connected; null when there is none to show.</summary>
    public string? SessionLabel
    {
        get => _sessionLabel;
        set
        {
            if (_sessionLabel == value)
            {
                return;
            }

            _sessionLabel = value;
            OnPropertyChanged();
            OnConnectionChanged();
        }
    }

    private void OnConnectionChanged()
    {
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(ConnectionHint));
        OnPropertyChanged(nameof(ConnectionTone));
        RefreshHome();
    }

    /// <summary>Puts "2/5" beside each widget heading: how many of its widgets are switched on, at a
    /// glance, so what is on screen never has to be read off twelve rows.</summary>
    private void UpdateWidgetCount()
    {
        foreach (var category in _slots.Values.GroupBy(slot => slot.Descriptor.Category))
        {
            var on = category.Count(slot => slot.IsEnabled);
            var total = category.Count();
            var section = NavSection.For(WidgetCatalog.HeadingOf(category.Key));
            section.Badge = $"{on}/{total}";
            section.BadgeTip = Loc.F("nav.section.badgeTip", on, total);
            section.HasActive = on > 0;
        }
    }

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
            OnPropertyChanged(nameof(ConnectionDegraded));
            OnConnectionChanged();
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
            OnConnectionChanged();
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
            // Once a second: catches a widget dragged off every monitor.
            RefreshHome();
        }
    }

    public int SelectedMonitorIndex => _selectedMonitorIndex;

    private double _interfaceScale = InterfaceScaleStore.Scale;

    /// <summary>How big the control panel draws itself (General › Window › Interface size).</summary>
    public double InterfaceScale
    {
        get => _interfaceScale;
        set
        {
            if (_interfaceScale.Equals(value))
            {
                return;
            }

            _interfaceScale = value;
            InterfaceScaleStore.Save(value);
            OnPropertyChanged();
        }
    }

    public DeltaReference DeltaReference => DeltaOptions.Reference;

    /// <summary>Timer period for the two displays whose whole value is latency — the proximity/ABS
    /// bars and the pedal trace. Index order matches the labels Settings › General › Performance offers.</summary>
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
        // A null arrives when the page is one the rail doesn't list (Help, at its foot): the list
        // loses its selection, and the page stays.
        if (item is null || ReferenceEquals(_selected, item))
        {
            return;
        }

        // Picking a page is the end of a search: the results give way to the page.
        ClearSearch();
        _selected = item;

        // The widget's own +/- control writes the same setting, so the page is rebuilt from current
        // state on every visit rather than trusting what it showed last time.
        item.Widget?.RefreshScaleFromWidget();
        BuildSettings(item);

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(RailSelected));
        OnPropertyChanged(nameof(PinnedRailSelected));
        OnPropertyChanged(nameof(SelectedWidget));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(IsHomePage));
        OnPropertyChanged(nameof(IsHelpPage));
        OnPropertyChanged(nameof(HomeScreen));
        OnPropertyChanged(nameof(PreviewLayout));
        OnPropertyChanged(nameof(HasLayoutPreview));
        OnPropertyChanged(nameof(PreviewLayoutResolution));
        OnPropertyChanged(nameof(ShowsPreviewPlaceholder));
        OnPropertyChanged(nameof(SelectedTab));
        OnPreviewTargetChanged();
    }

    private int _selectedTab;

    /// <summary>The open tab on a page that has tabs (Help); 0 elsewhere.</summary>
    public int SelectedTab
    {
        get => _selected?.HasTabs == true ? _selectedTab : 0;
        set
        {
            if (value < 0 || _selected is not { HasTabs: true } page || value == _selectedTab)
            {
                return;
            }

            _selectedTab = value;
            BuildSettings(page);
            OnPropertyChanged();
        }
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
        WidgetCatalog.PedalTrace => Configured(new PedalTraceWidget(), w => w.SetOptions(PedalTraceOptions)),
        WidgetCatalog.Incident => new IncidentWidget(),
        WidgetCatalog.TrackInfo => Configured(new TrackInfoWidget(), w => w.SetOptions(TrackInfoOptions)),
        WidgetCatalog.Weather => Configured(new WeatherWidget(), w => w.SetOptions(WeatherOptions)),
        WidgetCatalog.TrackMap => new TrackMapWidget(),
        WidgetCatalog.FuelCalculator => Configured(new FuelCalculatorWidget(), w => w.SetOptions(FuelCalculatorOptions)),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No factory registered for this widget."),
    };

    /// <summary>Codecs over this control panel's own options objects, saving through the real
    /// stores and raising <see cref="TableHeaderChanged"/> just as the widget pages do.</summary>
    internal IReadOnlyDictionary<string, IWidgetConfigCodec> CreateConfigCodecs() =>
        WidgetConfigCodecs.Create(new WidgetConfigTargets(
            StandingsOptions,
            RelativeOptions,
            FlagOptions,
            CockpitOptions,
            WeatherOptions,
            TrackInfoOptions,
            FuelCalculatorOptions,
            DeltaOptions,
            PedalTraceOptions,
            WidgetConfigPersistence.Stores,
            table => TableHeaderChanged?.Invoke(table)));

    private static T Configured<T>(T widget, Action<T> configure) where T : OverlayWindowBase
    {
        configure(widget);
        return widget;
    }

    private static string DescribeScreen(Screen screen, int index)
    {
        var bounds = screen.Bounds;
        var role = screen.Primary ? Loc.T("monitor.primary") : Loc.F("monitor.display", index + 1);
        return $"{bounds.Width}×{bounds.Height} ({role})";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>The colour of the toolbar's connection pill.</summary>
public enum ConnectionTone
{
    Waiting,
    Live,
    Degraded,
    Error,
}
