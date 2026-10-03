using System.Diagnostics;
using System.Globalization;
using System.IO;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// What each page offers, in one file.
///
/// This is the extension point the rest of the design exists to protect. Adding an option to a
/// widget is a single line here; adding a widget is an entry in <see cref="WidgetCatalog"/>, a
/// factory case, and — only if it has settings of its own — one case below. No XAML, no new handler,
/// no new field, and nothing to remember to add to a loop somewhere else.
///
/// Every page ends with the same PLACEMENT group. Consistency across widgets was an explicit goal:
/// wherever you are, size and auto-hide are in the same place, and they look the same.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private static readonly string[] SizeLadder = ScaleLevels.Labels;

    // Set while pages are built only to be searched: their settings are read and thrown away, so
    // nothing that wires a live setting up (hotkey rows, the dashboard button) may happen then.
    private bool _buildingIndex;

    private void BuildSettings(NavItem item)
    {
        item.Settings.Clear();

        var groups = BuildGroups(item).ToList();
        var term = _searchText.Trim();
        if (term.Length > 0 && !Contains(item.Title, term))
        {
            // Searching: the page shows just the groups that matched, whole, so each keeps its context.
            // A page found by its name shows everything; one found by its blurb still narrows.
            var matching = groups.Where(group => SearchTermsOf(group).Any(text => Contains(text, term))).ToList();
            if (matching.Count > 0)
            {
                groups = matching;
            }
        }

        foreach (var group in groups)
        {
            // Names every change in the activity trail after where the user made it.
            var path = $"{item.Title} › {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(group.Title.ToLowerInvariant())} › ";
            foreach (var setting in group.Items)
            {
                setting.TracePath = path;
                if (setting is ChipGroupSetting chips)
                {
                    foreach (var chip in chips.Chips)
                    {
                        chip.TracePath = $"{path}{chips.Label} › ";
                    }
                }
            }

            item.Settings.Add(group);
        }
    }

    private IEnumerable<SettingsGroup> BuildGroups(NavItem item)
    {
        if (item.Widget is not { } slot)
        {
            return item.Key switch
            {
                DashboardPageKey => DashboardPage(),
                LayoutsPageKey => LayoutsPage(),
                GeneralPageKey => GeneralPage(),
                WhatsNewPageKey => WhatsNewPage(),
                ChangelogPageKey => ChangelogPage(),
                AboutPageKey => AboutPage(),
                _ => [],
            };
        }

        return slot.Key switch
        {
            WidgetCatalog.Standings => [StandingsColumns(), StandingsTable(), Placement(slot)],
            WidgetCatalog.Relative => [RelativeColumns(), RelativeTable(), Placement(slot)],
            WidgetCatalog.FuelCalculator => [FuelCalculatorBlocks(), FuelCalculatorMath(), Placement(slot)],
            WidgetCatalog.Delta => [DeltaReferenceGroup(), Placement(slot)],
            WidgetCatalog.Flag => [FlagPreviewGroup(), FlagTypes(), FlagContent(), FlagLayoutGroup(), Placement(slot)],
            WidgetCatalog.Cockpit => [CockpitThemeGroup(), HighRateNote(), Placement(slot)],
            WidgetCatalog.PedalTrace => [HighRateNote(), Placement(slot)],
            WidgetCatalog.Weather => [WeatherElements(), WeatherDisplay(), Placement(slot)],
            _ => [Placement(slot)],
        };
    }

    // ===== Search =====

    private bool Matches(NavItem item, string term) =>
        Contains(item.Title, term) ||
        Contains(item.Blurb, term) ||
        (_searchIndex?.TryGetValue(item, out var text) == true && Contains(text, term));

    private Dictionary<NavItem, string> BuildSearchIndex()
    {
        _buildingIndex = true;
        try
        {
            return NavItems.ToDictionary(item => item, item => string.Join('\n', BuildGroups(item).SelectMany(SearchTermsOf)));
        }
        finally
        {
            _buildingIndex = false;
        }
    }

    private static IEnumerable<string> SearchTermsOf(SettingsGroup group)
    {
        yield return group.Title;
        if (group.Subtitle is { } subtitle)
        {
            yield return subtitle;
        }

        foreach (var item in group.Items)
        {
            // Release notes are prose about the app, not options in it.
            if (item is BulletSetting or ReleaseSetting)
            {
                continue;
            }

            yield return item.Label;
            if (item.Hint is { } hint)
            {
                yield return hint;
            }

            IEnumerable<string> extra = item switch
            {
                ChoiceSetting choice => choice.Options,
                SegmentedSetting segmented => segmented.Options,
                ChipGroupSetting chips => chips.Chips.SelectMany(chip => new[] { chip.Label, chip.Hint ?? "" }),
                HotkeySetting hotkey => [hotkey.Display],
                _ => [],
            };
            foreach (var text in extra)
            {
                yield return text;
            }
        }
    }

    private static bool Contains(string text, string term) => text.Contains(term, StringComparison.OrdinalIgnoreCase);

    // ===== Shared =====

    /// <summary>Size and auto-hide: the two things every widget has, laid out identically on every
    /// page so they can be changed without reading anything.</summary>
    private SettingsGroup Placement(WidgetSlot slot) => new SettingsGroup(
        "PLACEMENT",
        "Turn on Edit layout, then drag the widget to move it.")
        .With(
            new SegmentedSetting(
                "Size",
                null,
                SizeLadder,
                (int)slot.Scale,
                index => slot.Scale = (ScaleLevel)index),
            new SliderSetting(
                "Opacity",
                null,
                slot.Opacity,
                0,
                1,
                0.01,
                value => slot.Opacity = value),
            new ToggleSetting(
                "Hide when I'm not driving",
                "Hidden in menus, the garage, replays and while spectating.",
                slot.HideOutsideCar,
                value => slot.HideOutsideCar = value));

    // ===== Driver tables =====

    private SettingsGroup StandingsColumns() => new SettingsGroup("COLUMNS")
        .With(ColumnChips(StandingsOptions));

    private SettingsGroup RelativeColumns() => new SettingsGroup("COLUMNS")
        .With(ColumnChips(RelativeOptions));

    private static ChipGroupSetting ColumnChips(DriverTableOptions options) => new(
        "Visible columns",
        null,
        [
            Column("Pos", DriverTableColumn.Position, options.ShowPosition, options),
            Column("Car #", DriverTableColumn.CarNumber, options.ShowCarNumber, options),
            Column("Driver", DriverTableColumn.Driver, options.ShowDriver, options),
            Column("Last pit", DriverTableColumn.LastPitStop, options.ShowLastPitStop, options),
            Column("Tire", DriverTableColumn.TireCompound, options.ShowTireCompound, options),
            Column("iR", DriverTableColumn.IRating, options.ShowIRating, options),
            Column("iRΔ", DriverTableColumn.IRatingDelta, options.ShowIRatingDelta, options),
            Column("SR", DriverTableColumn.License, options.ShowLicense, options),
            Column("Lap", DriverTableColumn.Lap, options.ShowLap, options),
            Column("Best", DriverTableColumn.BestLap, options.ShowBestLap, options),
            Column("Last", DriverTableColumn.LastLap, options.ShowLastLap, options),
            Column("Gap", DriverTableColumn.Gap, options.ShowGap, options),
        ]);

    private static ChipSetting Column(string label, DriverTableColumn column, bool value, DriverTableOptions options) =>
        new(label, null, value, isVisible =>
        {
            options.SetVisible(column, isVisible);
            DriverTableOptionsStore.SaveColumn(options.Table, column, isVisible);
        });

    private SettingsGroup StandingsTable() => new SettingsGroup("TABLE")
        .With(
            new NumberSetting(
                "Drivers around me",
                "Shown around your position, besides the top 3.",
                StandingsOptions.FocusSize,
                DriverTableOptions.MinFocusSize,
                60,
                1,
                "0",
                null,
                value => SetFocusSize(StandingsOptions, value)),
            new ToggleSetting(
                "Split by class",
                "One block per class, each with its own top 3.",
                StandingsOptions.ShowMulticlass,
                value =>
                {
                    StandingsOptions.ShowMulticlass = value;
                    DriverTableOptionsStore.SaveMulticlass(DriverTable.Standings, value);
                }),
            ClassNameToggle(StandingsOptions),
            SessionIdToggle(StandingsOptions),
            SessionLapsToggle(StandingsOptions),
            SessionTimeToggle(StandingsOptions));

    private SettingsGroup RelativeTable() => new SettingsGroup("TABLE")
        .With(
            new NumberSetting(
                "Drivers each side",
                "Cars ahead and behind you.",
                RelativeOptions.FocusSize,
                DriverTableOptions.MinFocusSize,
                30,
                1,
                "0",
                null,
                value => SetFocusSize(RelativeOptions, value)),
            ClassNameToggle(RelativeOptions),
            SessionIdToggle(RelativeOptions),
            SessionLapsToggle(RelativeOptions),
            SessionTimeToggle(RelativeOptions));

    private ToggleSetting ClassNameToggle(DriverTableOptions options) => new(
        "Show category name",
        "Your class, e.g. GT3, next to the title.",
        options.ShowClassName,
        value =>
        {
            options.ShowClassName = value;
            DriverTableOptionsStore.SaveClassName(options.Table, value);
            TableHeaderChanged?.Invoke(options.Table);
        });

    private ToggleSetting SessionIdToggle(DriverTableOptions options) => new(
        "Show session number",
        "The subsession id. iRacing doesn't report the split.",
        options.ShowSessionId,
        value =>
        {
            options.ShowSessionId = value;
            DriverTableOptionsStore.SaveSessionId(options.Table, value);
            TableHeaderChanged?.Invoke(options.Table);
        });

    private static ToggleSetting SessionLapsToggle(DriverTableOptions options) => new(
        "Show laps",
        "Current lap over total. Estimated in timed races.",
        options.ShowSessionLaps,
        value =>
        {
            options.ShowSessionLaps = value;
            DriverTableOptionsStore.SaveSessionLaps(options.Table, value);
        });

    private static ToggleSetting SessionTimeToggle(DriverTableOptions options) => new(
        "Show session time",
        "Elapsed over total.",
        options.ShowSessionTime,
        value =>
        {
            options.ShowSessionTime = value;
            DriverTableOptionsStore.SaveSessionTime(options.Table, value);
        });

    private static void SetFocusSize(DriverTableOptions options, double value)
    {
        options.FocusSize = (int)Math.Round(value);
        DriverTableOptionsStore.SaveFocusSize(options.Table, options.FocusSize);
    }

    // ===== Fuel calculator =====

    private SettingsGroup FuelCalculatorBlocks() => new SettingsGroup("BLOCKS")
        .With(new ChipGroupSetting(
            "Visible blocks",
            null,
            [
                FuelBlock("Fuel bar", FuelCalculatorOptions.ShowFuelBar, v => FuelCalculatorOptions.ShowFuelBar = v),
                FuelBlock("Remaining", FuelCalculatorOptions.ShowFuelRemaining, v => FuelCalculatorOptions.ShowFuelRemaining = v),
                FuelBlock("Last lap", FuelCalculatorOptions.ShowLastLap, v => FuelCalculatorOptions.ShowLastLap = v),
                FuelBlock("Average", FuelCalculatorOptions.ShowAverage, v => FuelCalculatorOptions.ShowAverage = v),
                FuelBlock("Minimum", FuelCalculatorOptions.ShowMinimum, v => FuelCalculatorOptions.ShowMinimum = v),
                FuelBlock("Maximum", FuelCalculatorOptions.ShowMaximum, v => FuelCalculatorOptions.ShowMaximum = v),
                FuelBlock("Laps left", FuelCalculatorOptions.ShowLapsRemaining, v => FuelCalculatorOptions.ShowLapsRemaining = v),
                FuelBlock("To finish", FuelCalculatorOptions.ShowFuelToFinish, v => FuelCalculatorOptions.ShowFuelToFinish = v),
                FuelBlock("Refuel", FuelCalculatorOptions.ShowRefuel, v => FuelCalculatorOptions.ShowRefuel = v),
            ]));

    private ChipSetting FuelBlock(string label, bool value, Action<bool> assign) =>
        new(label, null, value, isVisible =>
        {
            assign(isVisible);
            FuelCalculatorOptionsStore.Save(FuelCalculatorOptions);
        });

    private SettingsGroup FuelCalculatorMath() => new SettingsGroup("CALCULATION")
        .With(
            new ChoiceSetting(
                "Average over",
                "Recent laps react faster to fuel saving.",
                ["Whole session", "Last 3 laps", "Last 5 laps", "Last 10 laps"],
                (int)FuelCalculatorOptions.AverageSource,
                index =>
                {
                    FuelCalculatorOptions.AverageSource = (FuelAverageSource)index;
                    FuelCalculatorOptionsStore.Save(FuelCalculatorOptions);
                }),
            new NumberSetting(
                "Safety margin",
                "Scales with consumption.",
                FuelCalculatorOptions.MarginLaps, 0, 20, 0.5, "0.#", "laps",
                value =>
                {
                    FuelCalculatorOptions.MarginLaps = value;
                    FuelCalculatorOptionsStore.Save(FuelCalculatorOptions);
                }),
            new NumberSetting(
                "Extra reserve",
                "A flat amount on top of the margin.",
                Units.Volume(FuelCalculatorOptions.MarginLiters, Units.Current),
                0,
                Units.Current == UnitSystem.Imperial ? 13 : 50,
                Units.Current == UnitSystem.Imperial ? 0.1 : 0.5,
                "0.#",
                Units.VolumeUnit(Units.Current),
                value =>
                {
                    // Stored in litres, like every fuel figure, whatever the display units.
                    FuelCalculatorOptions.MarginLiters = Units.VolumeToLiters(value, Units.Current);
                    FuelCalculatorOptionsStore.Save(FuelCalculatorOptions);
                }));

    // ===== Weather =====

    private static readonly string[] GraphicSizes = ["S", "M", "L"];

    private SettingsGroup WeatherElements() => new SettingsGroup("ELEMENTS")
        .With(new ChipGroupSetting(
            "Visible elements",
            null,
            [
                WeatherChip("Air temp", WeatherOptions.ShowAirTemp, v => WeatherOptions.ShowAirTemp = v),
                WeatherChip("Track temp", WeatherOptions.ShowTrackTemp, v => WeatherOptions.ShowTrackTemp = v),
                WeatherChip("Humidity", WeatherOptions.ShowHumidity, v => WeatherOptions.ShowHumidity = v),
                WeatherChip("Wind speed", WeatherOptions.ShowWindSpeed, v => WeatherOptions.ShowWindSpeed = v),
                WeatherChip("Wind arrow", WeatherOptions.ShowWindArrow, v => WeatherOptions.ShowWindArrow = v),
                WeatherChip("Forecast icon", WeatherOptions.ShowForecast, v => WeatherOptions.ShowForecast = v),
                WeatherChip("Rain chance", WeatherOptions.ShowRainProbability, v => WeatherOptions.ShowRainProbability = v),
                WeatherChip("Track surface", WeatherOptions.ShowTrackWetness, v => WeatherOptions.ShowTrackWetness = v),
            ]));

    private ChipSetting WeatherChip(string label, bool value, Action<bool> assign) =>
        new(label, null, value, isVisible => SaveWeather(() => assign(isVisible)));

    private SettingsGroup WeatherDisplay() => new SettingsGroup("DISPLAY")
        .With(
            new ToggleSetting(
                "Show labels",
                "Captions such as AIR and TRACK.",
                WeatherOptions.ShowLabels,
                value => SaveWeather(() => WeatherOptions.ShowLabels = value)),
            new ToggleSetting(
                "Compact",
                "One horizontal strip.",
                WeatherOptions.Compact,
                value => SaveWeather(() => WeatherOptions.Compact = value)),
            new SegmentedSetting(
                "Forecast icon size",
                null,
                GraphicSizes,
                (int)WeatherOptions.IconSize,
                index => SaveWeather(() => WeatherOptions.IconSize = (WeatherGraphicSize)index)),
            new SegmentedSetting(
                "Wind arrow size",
                null,
                GraphicSizes,
                (int)WeatherOptions.ArrowSize,
                index => SaveWeather(() => WeatherOptions.ArrowSize = (WeatherGraphicSize)index)));

    private void SaveWeather(Action change)
    {
        change();
        WeatherOptionsStore.Save(WeatherOptions);
    }

    // ===== Delta =====

    private SettingsGroup DeltaReferenceGroup() => new SettingsGroup("REFERENCE")
        .With(new ChoiceSetting(
            "Compare against",
            null,
            ["Session best lap", "Personal best (all-time)", "Optimal lap"],
            (int)_deltaReference,
            index => _deltaReference = (DeltaReference)index));

    // ===== Cockpit =====

    // Index order matches CockpitTheme.
    private static readonly string[] CockpitThemeNames =
    [
        "Default", "GT Sports", "Casual", "Hypercar", "Pit Wall", "Classic Car", "Invisible",
    ];

    private SettingsGroup CockpitThemeGroup() => new SettingsGroup(
        "THEME",
        "Each has its own layout and size.")
        .With(new ChoiceSetting(
            "Cockpit theme",
            null,
            CockpitThemeNames,
            (int)CockpitOptions.Theme,
            index =>
            {
                CockpitOptions.Theme = (CockpitTheme)index;
                CockpitThemeStore.Save(CockpitOptions.Theme);
            }));

    // ===== Flags =====

    private static readonly (FlagGroup Group, string Label, string Hint)[] FlagGroupLabels =
    [
        (FlagGroup.Track, "Track status", "The most serious one shows."),
        (FlagGroup.Driver, "Aimed at you", "Penalties and warnings for your car."),
        (FlagGroup.Race, "Race progress", "Laps to go, halfway, last lap, finish."),
        (FlagGroup.Advisory, "Advisories", "Can stack with any other flag."),
    ];

    private SettingsGroup FlagPreviewGroup() => new SettingsGroup("PREVIEW")
        .With(new ChoiceSetting(
            "Simulate",
            "Preview any flag without iRacing.",
            PreviewData.FlagScenarios.Select(s => s.Label).ToList(),
            FlagPreview.Index,
            index => FlagPreview.Index = index));

    private SettingsGroup FlagTypes()
    {
        var group = new SettingsGroup(
            "FLAG TYPES",
            "Switched-off flags never appear. Each category shows one at a time.");

        foreach (var (flagGroup, label, hint) in FlagGroupLabels)
        {
            group.Items.Add(new ChipGroupSetting(
                label,
                hint,
                FlagCatalog.All
                    .Where(definition => definition.Group == flagGroup)
                    .Select(definition => new ChipSetting(
                        definition.Label,
                        definition.Description,
                        FlagOptions.IsEnabled(definition.Kind),
                        enabled => SaveFlags(() => FlagOptions.SetEnabled(definition.Kind, enabled))))
                    .ToList()));
        }

        return group;
    }

    private SettingsGroup FlagContent() => new SettingsGroup("CONTENT")
        .With(
            new ChoiceSetting(
                "Show",
                null,
                ["Icon and text", "Icon only"],
                (int)FlagOptions.DisplayMode,
                index => SaveFlags(() => FlagOptions.DisplayMode = (FlagDisplayMode)index)),
            new ToggleSetting(
                "Flag name",
                null,
                FlagOptions.ShowName,
                value => SaveFlags(() => FlagOptions.ShowName = value)),
            new ToggleSetting(
                "Event description",
                "What the flag asks of you.",
                FlagOptions.ShowDescription,
                value => SaveFlags(() => FlagOptions.ShowDescription = value)),
            new NumberSetting(
                "Flags at once",
                "Most important first, and larger.",
                FlagOptions.MaxFlags,
                1,
                FlagOptions.MaxFlagsLimit,
                1,
                "0",
                null,
                value => SaveFlags(() => FlagOptions.MaxFlags = (int)Math.Round(value))),
            new ChoiceSetting(
                "Info flags stay up",
                "Green, halfway and laps-to-go. Safety flags stay while out.",
                FlagOptions.HoldChoices.Select(s => s == 0 ? "While active" : $"{s} seconds").ToList(),
                Math.Max(0, FlagOptions.HoldChoices.ToList().IndexOf(FlagOptions.InfoFlagSeconds)),
                index => SaveFlags(() => FlagOptions.InfoFlagSeconds = FlagOptions.HoldChoices[index])));

    private SettingsGroup FlagLayoutGroup() => new SettingsGroup("LAYOUT")
        .With(
            new ChoiceSetting(
                "Stack flags",
                null,
                ["Vertically", "Side by side"],
                (int)FlagOptions.Layout,
                index => SaveFlags(() => FlagOptions.Layout = (FlagLayout)index)),
            new ChoiceSetting(
                "Icon position",
                null,
                ["Left of the text", "Above the text"],
                (int)FlagOptions.IconPlacement,
                index => SaveFlags(() => FlagOptions.IconPlacement = (FlagIconPlacement)index)));

    private void SaveFlags(Action change)
    {
        change();
        FlagOptionsStore.Save(FlagOptions);
    }

    // ===== Application pages =====

    private SettingsGroup HighRateNote() => new SettingsGroup(
        "UPDATE RATE",
        "Set in General › Performance.");

    /// <summary>Everything about the application rather than one widget, on one page: the window,
    /// units, performance, hotkeys and diagnostics.</summary>
    private IEnumerable<SettingsGroup> GeneralPage() =>
    [
        new SettingsGroup(
            "WINDOW",
            "In the tray, overlays keep running. Exit from the tray menu.")
            .With(
                new ChoiceSetting(
                    "Close button behavior",
                    null,
                    ["Minimize to system tray (recommended)", "Exit application"],
                    (int)TrayPreferencesStore.CloseBehavior,
                    index => TrayPreferencesStore.SaveCloseBehavior((CloseBehavior)index)),
                new ActionSetting(
                    "Configuration folder",
                    "Layouts, options and hotkeys.",
                    "Open folder",
                    OpenConfigFolder)),
        .. UnitsGroups(),
        .. PerformanceGroups(),
        .. HotkeyGroups(),
        DiagnosticsGroup(),
    ];

    /// <summary>Everything a bug report needs, one click away. The report masks the Windows user
    /// folder, so it can be pasted into a public issue as is.</summary>
    private static SettingsGroup DiagnosticsGroup()
    {
        ActionSetting? copy = null;
        copy = new ActionSetting(
            "Copy diagnostics",
            "A text report for a bug report.",
            "Copy",
            () =>
            {
                System.Windows.Clipboard.SetText(DiagnosticsReport.Build("Copied by the user"));
                copy!.ButtonText = "Copied";
            });

        var problems = AppLog.Problems();
        var errors = problems.Where(p => p.Level >= LogLevel.Error).Sum(p => p.Count);
        var warnings = problems.Where(p => p.Level == LogLevel.Warning).Sum(p => p.Count);
        return new SettingsGroup(
            "DIAGNOSTICS",
            $"This run: {AppInfo.RunId} · {errors} {(errors == 1 ? "error" : "errors")} · {warnings} {(warnings == 1 ? "warning" : "warnings")}. " +
            "Logs stay on your PC.")
            .With(
                copy,
                new ActionSetting(
                    "Export report",
                    "A .zip with logs, crash reports and settings.",
                    "Export",
                    () => ShowInExplorer(DiagnosticsReport.Export())),
                new ActionSetting(
                    "Log files",
                    "One per day, kept 30 days.",
                    "Open folder",
                    () =>
                    {
                        Directory.CreateDirectory(AppLog.LogDirectory);
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppLog.LogDirectory}\"") { UseShellExecute = true });
                    }));
    }

    private static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    /// <summary>Opens the settings folder in Explorer, creating it first on a fresh install.</summary>
    public static void OpenConfigFolder()
    {
        Directory.CreateDirectory(TrayPreferencesStore.ConfigFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TrayPreferencesStore.ConfigFolder}\"") { UseShellExecute = true });
    }

    private IEnumerable<SettingsGroup> UnitsGroups() =>
    [
        new SettingsGroup("UNITS")
            .With(new SegmentedSetting(
                "Units",
                Units.IRacingSetting is { } iracing
                    ? $"iRacing is set to {(iracing == UnitSystem.Imperial ? "imperial" : "metric")}."
                    : "Metric until iRacing reports its setting.",
                ["Follow iRacing", "Metric", "Imperial"],
                (int)Units.Preference,
                index =>
                {
                    var preference = (UnitPreference)index;
                    UnitPreferenceStore.Save(preference);
                    Units.SetPreference(preference);
                })),
    ];

    private IEnumerable<SettingsGroup> PerformanceGroups() =>
    [
        new SettingsGroup(
            "PERFORMANCE · HIGH-RATE DISPLAYS",
            "Proximity bars, ABS light and pedal trace.")
            .With(new ChoiceSetting(
                "Refresh rate",
                "Faster costs more CPU.",
                ["Fastest (~60 Hz)", "Fast (30 Hz)", "Normal (15 Hz)", "Slow (10 Hz)", "Slowest (5 Hz)"],
                _criticalRefreshIndex,
                index =>
                {
                    _criticalRefreshIndex = index;
                    CriticalRefreshStore.Save(index);
                    CriticalRefreshChanged?.Invoke(CriticalRefreshIntervalMs);
                })),
        new SettingsGroup(
            "PERFORMANCE · WIND COMPASS",
            "The Weather widget's wind arrow, which turns with your car.")
            .With(new ChoiceSetting(
                "Refresh rate",
                "Fixed rates skip the animation and use less GPU.",
                ["Smooth (animated)", "10 Hz", "5 Hz", "2 Hz", "1 Hz"],
                _compassRefreshIndex,
                index =>
                {
                    _compassRefreshIndex = index;
                    CompassRefreshStore.Save(index);
                    WidgetOf<WeatherWidget>(WidgetCatalog.Weather)?.SetCompassInterval(CompassRefreshIntervalMs);
                })),
    ];

    private IEnumerable<SettingsGroup> DashboardPage()
    {
        var button = new ActionSetting(
            "Second-monitor dashboard",
            "Every panel at once.",
            "Show dashboard",
            () => DashboardToggleRequested?.Invoke());
        if (!_buildingIndex)
        {
            DashboardButton = button;
        }

        return
        [
            new SettingsGroup("OUTPUT")
                .With(
                    new ChoiceSetting(
                        "Monitor",
                        null,
                        MonitorNames,
                        _selectedMonitorIndex,
                        index => _selectedMonitorIndex = index),
                    new ChoiceSetting(
                        "Theme",
                        "Floating widgets aren't affected.",
                        ["Classic", "Digital HUD", "Raw DIY"],
                        (int)_dashboardTheme,
                        index =>
                        {
                            _dashboardTheme = (DashboardTheme)index;
                            DashboardThemeStore.Save(_dashboardTheme);
                            DashboardThemeChanged?.Invoke(_dashboardTheme);
                        }),
                    button),
        ];
    }
}
