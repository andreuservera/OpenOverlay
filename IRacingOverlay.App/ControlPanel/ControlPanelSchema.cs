using System.Diagnostics;
using System.Globalization;
using System.IO;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

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
    private static readonly string[] SizeLadder = ["XS", "S", "M", "L", "XL"];

    private void BuildSettings(NavItem item)
    {
        item.Settings.Clear();

        foreach (var group in BuildGroups(item))
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
                GeneralPageKey => GeneralPage(),
                PerformancePageKey => PerformancePage(),
                UnitsPageKey => UnitsPage(),
                HotkeysPageKey => HotkeysPage(),
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

    // ===== Shared =====

    /// <summary>Size and auto-hide: the two things every widget has, laid out identically on every
    /// page so they can be changed without reading anything.</summary>
    private SettingsGroup Placement(WidgetSlot slot) => new SettingsGroup(
        "PLACEMENT",
        "Drag the widget itself to move it — turn on Edit layout in the toolbar first.")
        .With(
            new SegmentedSetting(
                "Size",
                "Five fixed steps. Everything inside scales together, so the widget looks the same at every size.",
                SizeLadder,
                (int)slot.Scale,
                index => slot.Scale = (ScaleLevel)index),
            new SliderSetting(
                "Opacity",
                "Applies to the whole widget — background, borders, text and graphics together.",
                slot.Opacity,
                0,
                1,
                0.01,
                value => slot.Opacity = value),
            new ToggleSetting(
                "Hide when I'm not driving",
                "Disappears whenever you aren't at the wheel: iRacing closed, in the menus, in the garage, spectating or watching a replay. Comes back the moment you're in the car, pit lane included.",
                slot.HideOutsideCar,
                value => slot.HideOutsideCar = value));

    // ===== Driver tables =====

    private SettingsGroup StandingsColumns() => new SettingsGroup(
        "COLUMNS",
        "Hidden columns give their width back to the rest of the table.")
        .With(ColumnChips(StandingsOptions));

    private SettingsGroup RelativeColumns() => new SettingsGroup(
        "COLUMNS",
        "Relative keeps its own column set — it answers a different question from Standings.")
        .With(ColumnChips(RelativeOptions));

    private static ChipGroupSetting ColumnChips(DriverTableOptions options) => new(
        "Visible columns",
        null,
        [
            Column("Pos", DriverTableColumn.Position, options.ShowPosition, options),
            Column("Car #", DriverTableColumn.CarNumber, options.ShowCarNumber, options),
            Column("Driver", DriverTableColumn.Driver, options.ShowDriver, options),
            Column("Last pit", DriverTableColumn.LastPitStop, options.ShowLastPitStop, options),
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
                "How many cars show around your position, on top of the always-visible top 3.",
                StandingsOptions.FocusSize,
                DriverTableOptions.MinFocusSize,
                60,
                1,
                "0",
                null,
                value => SetFocusSize(StandingsOptions, value)),
            new ToggleSetting(
                "Split by class",
                "One block per class, each with its own header and top 3. No effect in a single-class session.",
                StandingsOptions.ShowMulticlass,
                value =>
                {
                    StandingsOptions.ShowMulticlass = value;
                    DriverTableOptionsStore.SaveMulticlass(DriverTable.Standings, value);
                }),
            CarNameToggle(StandingsOptions),
            SessionIdToggle(StandingsOptions));

    private SettingsGroup RelativeTable() => new SettingsGroup("TABLE")
        .With(
            new NumberSetting(
                "Drivers each side",
                "Cars ahead and behind. The table holds that many rows so its height stops changing mid-race.",
                RelativeOptions.FocusSize,
                DriverTableOptions.MinFocusSize,
                30,
                1,
                "0",
                null,
                value => SetFocusSize(RelativeOptions, value)),
            CarNameToggle(RelativeOptions),
            SessionIdToggle(RelativeOptions));

    private ToggleSetting CarNameToggle(DriverTableOptions options) => new(
        "Show car name",
        "Next to the title. Single-class sessions only — in multiclass the class headers already say it.",
        options.ShowCarName,
        value =>
        {
            options.ShowCarName = value;
            DriverTableOptionsStore.SaveCarName(options.Table, value);
            TableHeaderChanged?.Invoke(options.Table);
        });

    private ToggleSetting SessionIdToggle(DriverTableOptions options) => new(
        "Show session number",
        "The subsession id of the room you're in. iRacing's telemetry exposes no split number.",
        options.ShowSessionId,
        value =>
        {
            options.ShowSessionId = value;
            DriverTableOptionsStore.SaveSessionId(options.Table, value);
            TableHeaderChanged?.Invoke(options.Table);
        });

    private static void SetFocusSize(DriverTableOptions options, double value)
    {
        options.FocusSize = (int)Math.Round(value);
        DriverTableOptionsStore.SaveFocusSize(options.Table, options.FocusSize);
    }

    // ===== Fuel calculator =====

    private SettingsGroup FuelCalculatorBlocks() => new SettingsGroup(
        "BLOCKS",
        "Every block is independent, so the same widget can be a one-line laps-left readout or a full strategy box.")
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

    private SettingsGroup FuelCalculatorMath() => new SettingsGroup(
        "CALCULATION",
        "Both margins apply together: laps scale with consumption, liters are a flat reserve.")
        .With(
            new ChoiceSetting(
                "Average over",
                "A rolling window reacts faster once you start saving fuel; the whole session is steadier.",
                ["Whole session", "Last 3 laps", "Last 5 laps", "Last 10 laps"],
                (int)FuelCalculatorOptions.AverageSource,
                index =>
                {
                    FuelCalculatorOptions.AverageSource = (FuelAverageSource)index;
                    FuelCalculatorOptionsStore.Save(FuelCalculatorOptions);
                }),
            new NumberSetting(
                "Safety margin",
                null,
                FuelCalculatorOptions.MarginLaps, 0, 20, 0.5, "0.#", "laps",
                value =>
                {
                    FuelCalculatorOptions.MarginLaps = value;
                    FuelCalculatorOptionsStore.Save(FuelCalculatorOptions);
                }),
            new NumberSetting(
                "Extra reserve",
                "In your selected units (see the Units page).",
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

    private SettingsGroup WeatherElements() => new SettingsGroup(
        "ELEMENTS",
        "Each one is independent; the widget closes up around whatever is hidden.")
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
            ]));

    private ChipSetting WeatherChip(string label, bool value, Action<bool> assign) =>
        new(label, null, value, isVisible => SaveWeather(() => assign(isVisible)));

    private SettingsGroup WeatherDisplay() => new SettingsGroup(
        "DISPLAY",
        "The compass turns with your car; the arrow runs from where the wind comes from to where it blows. Units are set on the Units page.")
        .With(
            new ToggleSetting(
                "Show labels",
                "AIR, TRACK, WIND and RAIN captions. Off leaves only the values and icons.",
                WeatherOptions.ShowLabels,
                value => SaveWeather(() => WeatherOptions.ShowLabels = value)),
            new ToggleSetting(
                "Compact",
                "One horizontal strip instead of stacked sections.",
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

    private SettingsGroup DeltaReferenceGroup() => new SettingsGroup(
        "REFERENCE",
        "The lap the live delta is measured against.")
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
        "Each theme is a different dashboard — its own layout, shape and size.")
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
        (FlagGroup.Track, "Track status", "One at a time — the most serious wins."),
        (FlagGroup.Driver, "Aimed at you", "Penalties and warnings for your car. One at a time."),
        (FlagGroup.Race, "Race progress", "Laps-to-go boards, halfway, last lap, finish. One at a time."),
        (FlagGroup.Advisory, "Advisories", "Shown alongside everything else."),
    ];

    private SettingsGroup FlagPreviewGroup() => new SettingsGroup("PREVIEW")
        .With(new ChoiceSetting(
            "Simulate",
            "Preview only — shows any flag exactly as the overlay draws it, no iRacing needed.",
            PreviewData.FlagScenarios.Select(s => s.Label).ToList(),
            FlagPreview.Index,
            index => FlagPreview.Index = index));

    private SettingsGroup FlagTypes()
    {
        var group = new SettingsGroup(
            "FLAG TYPES",
            "A flag switched off never appears — on the widget or on the dashboard.");

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
                "Icon only is the most compact: the flag graphic and its colour bar, nothing else.",
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
                "One line under the name saying what the flag asks of you.",
                FlagOptions.ShowDescription,
                value => SaveFlags(() => FlagOptions.ShowDescription = value)),
            new NumberSetting(
                "Flags at once",
                "Most important first, and the first one a size up.",
                FlagOptions.MaxFlags,
                1,
                FlagOptions.MaxFlagsLimit,
                1,
                "0",
                null,
                value => SaveFlags(() => FlagOptions.MaxFlags = (int)Math.Round(value))),
            new ChoiceSetting(
                "Info flags stay up",
                "Green, halfway, laps-to-go boards and waving flags. Safety flags always stay up while they're out.",
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
        "This widget reads telemetry on its own timer — set it on the Performance page.");

    private IEnumerable<SettingsGroup> GeneralPage() =>
    [
        new SettingsGroup(
            "WINDOW",
            "Overlays and telemetry keep running while this window is in the tray. Exit from the tray icon's menu.")
            .With(
                new ChoiceSetting(
                    "Close button behavior",
                    "What the X on this window does.",
                    ["Minimize to system tray (recommended)", "Exit application"],
                    (int)TrayPreferencesStore.CloseBehavior,
                    index => TrayPreferencesStore.SaveCloseBehavior((CloseBehavior)index)),
                new ActionSetting(
                    "Configuration folder",
                    "Where layouts, options and hotkeys are saved.",
                    "Open folder",
                    OpenConfigFolder)),
        DiagnosticsGroup(),
    ];

    /// <summary>Everything a bug report needs, one click away. The report masks the Windows user
    /// folder, so it can be pasted into a public issue as is.</summary>
    private static SettingsGroup DiagnosticsGroup()
    {
        ActionSetting? copy = null;
        copy = new ActionSetting(
            "Copy diagnostics",
            "Version, telemetry and widget health, and the recent log, as text for a bug report.",
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
            "Every run is logged: what you changed, what the app did, and each error with a reference (shown in the status bar) to look it up by. " +
            $"This run: {AppInfo.RunId} · {errors} {(errors == 1 ? "error" : "errors")} · {warnings} {(warnings == 1 ? "warning" : "warnings")}. " +
            "Nothing leaves your PC unless you send it.")
            .With(
                copy,
                new ActionSetting(
                    "Export report",
                    "A .zip with the report, the logs, crash reports and your settings, shown in Explorer.",
                    "Export",
                    () => ShowInExplorer(DiagnosticsReport.Export())),
                new ActionSetting(
                    "Log files",
                    "One file per day, kept for 30 days. Each line carries the run it belongs to.",
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

    private IEnumerable<SettingsGroup> UnitsPage() =>
    [
        new SettingsGroup(
            "UNITS",
            "Speed, temperature, tire pressure and fuel in every overlay, the dashboard and the previews.")
            .With(new SegmentedSetting(
                "Units",
                Units.IRacingSetting is { } iracing
                    ? $"iRacing is currently set to {(iracing == UnitSystem.Imperial ? "imperial" : "metric")}. A fixed choice here always wins."
                    : "Following iRacing shows metric until the sim reports its setting. A fixed choice here always wins.",
                ["Follow iRacing", "Metric", "Imperial"],
                (int)Units.Preference,
                index =>
                {
                    var preference = (UnitPreference)index;
                    UnitPreferenceStore.Save(preference);
                    Units.SetPreference(preference);
                })),
    ];

    private IEnumerable<SettingsGroup> PerformancePage() =>
    [
        new SettingsGroup(
            "HIGH-RATE DISPLAYS",
            "The proximity bars, the ABS light and the pedal trace are the displays where update rate is the whole point. They run on their own timer, independent of everything else.")
            .With(new ChoiceSetting(
                "Refresh rate",
                "Faster is lower latency and more CPU.",
                ["Fastest (~60 Hz)", "Fast (30 Hz)", "Normal (15 Hz)", "Slow (10 Hz)", "Slowest (5 Hz)"],
                _criticalRefreshIndex,
                index =>
                {
                    _criticalRefreshIndex = index;
                    CriticalRefreshStore.Save(index);
                    CriticalRefreshChanged?.Invoke(CriticalRefreshIntervalMs);
                })),
    ];

    private IEnumerable<SettingsGroup> DashboardPage()
    {
        DashboardButton = new ActionSetting(
            "Second-monitor dashboard",
            "A fullscreen layout with every panel at once, separate from the floating widgets.",
            "Show dashboard",
            () => DashboardToggleRequested?.Invoke());

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
                        "Only the dashboard changes — floating widgets always keep the standard look.",
                        ["Classic", "Digital HUD", "Raw DIY"],
                        (int)_dashboardTheme,
                        index =>
                        {
                            _dashboardTheme = (DashboardTheme)index;
                            DashboardThemeStore.Save(_dashboardTheme);
                            DashboardThemeChanged?.Invoke(_dashboardTheme);
                        }),
                    DashboardButton),
        ];
    }
}
