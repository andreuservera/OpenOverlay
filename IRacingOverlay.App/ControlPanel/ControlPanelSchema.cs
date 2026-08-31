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
                PerformancePageKey => PerformancePage(),
                _ => [],
            };
        }

        return slot.Key switch
        {
            WidgetCatalog.Standings => [StandingsColumns(), StandingsTable(), Placement(slot)],
            WidgetCatalog.Relative => [RelativeColumns(), RelativeTable(), Placement(slot)],
            WidgetCatalog.FuelCalculator => [FuelCalculatorBlocks(), FuelCalculatorMath(), Placement(slot)],
            WidgetCatalog.Delta => [DeltaReferenceGroup(), Placement(slot)],
            WidgetCatalog.Cockpit or WidgetCatalog.PedalTrace => [HighRateNote(), Placement(slot)],
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
                null,
                FuelCalculatorOptions.MarginLiters, 0, 50, 0.5, "0.#", "L",
                value =>
                {
                    FuelCalculatorOptions.MarginLiters = value;
                    FuelCalculatorOptionsStore.Save(FuelCalculatorOptions);
                }));

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

    // ===== Application pages =====

    private SettingsGroup HighRateNote() => new SettingsGroup(
        "UPDATE RATE",
        "This widget reads telemetry on its own timer — set it on the Performance page.");

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
