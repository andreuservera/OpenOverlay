using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// The options objects a widget's settings act on, and what happens after each change.
///
/// The widget's own page in the control panel uses the shared, live options with
/// <see cref="SaveToStores"/> on: every change is saved and announced exactly as it always has been.
/// The layout editor uses the options behind its preview of one layout widget, saving nothing — its
/// <see cref="Changed"/> records the change in the layout instead, where undo and Save pick it up.
/// </summary>
/// <param name="SaveToStores">Whether each change is written to the widget's settings store.</param>
/// <param name="TableHeaderChanged">Raised when a driver table's header field is switched, as the
/// page has always done; null where there is no live widget to nudge.</param>
/// <param name="Changed">Called after every change has been applied (and saved, if saving).</param>
public sealed record WidgetSettingsContext(
    DriverTableOptions Standings,
    DriverTableOptions Relative,
    FuelCalculatorOptions FuelCalculator,
    FlagOptions Flag,
    CockpitOptions Cockpit,
    WeatherOptions Weather,
    TrackInfoOptions TrackInfo,
    DeltaOptions Delta,
    bool SaveToStores,
    Action<DriverTable>? TableHeaderChanged = null,
    Action? Changed = null)
{
    /// <summary>Saves through <paramref name="save"/> if this context saves, then reports the change.</summary>
    internal void Persist(Action save)
    {
        if (SaveToStores)
        {
            save();
        }

        Changed?.Invoke();
    }
}

/// <summary>
/// The settings groups that configure a widget itself — the same rows on its control panel page and
/// in the layout editor, built once here so the two can never offer different options. What is not
/// here is what does not belong to a layout: PLACEMENT (size, opacity, auto-hide), which the page
/// binds to the widget's slot and the editor to the layout entry, and the flag preview scenario.
/// </summary>
public static class WidgetSettings
{
    /// <summary>The widget's own settings groups, in the order its page shows them. Empty for a
    /// widget with no options of its own.</summary>
    public static IReadOnlyList<SettingsGroup> For(string type, WidgetSettingsContext context) => type switch
    {
        WidgetCatalog.Standings => [StandingsColumns(context), StandingsTable(context)],
        WidgetCatalog.Relative => [RelativeColumns(context), RelativeTable(context)],
        WidgetCatalog.FuelCalculator => [FuelCalculatorBlocks(context), FuelCalculatorDisplay(context), FuelCalculatorMath(context)],
        WidgetCatalog.Flag => [FlagTypes(context), FlagContent(context), FlagLayoutGroup(context)],
        WidgetCatalog.Cockpit => [CockpitThemeGroup(context), HighRateNote()],
        WidgetCatalog.PedalTrace => [HighRateNote()],
        WidgetCatalog.Weather => [WeatherElements(context), WeatherDisplay(context)],
        WidgetCatalog.TrackInfo => [TrackInfoElements(context)],
        WidgetCatalog.Delta => [DeltaReferenceGroup(context)],
        _ => [],
    };

    // ===== Driver tables =====

    private static SettingsGroup StandingsColumns(WidgetSettingsContext context) => new SettingsGroup("COLUMNS")
        .With(ColumnList(context.Standings, context));

    private static SettingsGroup RelativeColumns(WidgetSettingsContext context) => new SettingsGroup("COLUMNS")
        .With(ColumnList(context.Relative, context));

    private static ReorderListSetting ColumnList(DriverTableOptions options, WidgetSettingsContext context) => new(
        "Visible columns",
        "Drag a row by its handle to change the order.",
        options.ColumnOrder.Select(column => column == DriverTableColumn.IRating
            ? new ReorderListItem(
                column,
                Column("iRating", DriverTableColumn.IRating, options, context),
                Column("Δ", DriverTableColumn.IRatingDelta, options, context))
            : new ReorderListItem(column, Column(ColumnLabel(column), column, options, context))),
        order =>
        {
            options.ColumnOrder = order.Cast<DriverTableColumn>().ToList();
            context.Persist(() => DriverTableOptionsStore.SaveColumnOrder(options.Table, options.ColumnOrder));
        });

    private static string ColumnLabel(DriverTableColumn column) => column switch
    {
        DriverTableColumn.Position => "Pos",
        DriverTableColumn.CarNumber => "Car #",
        DriverTableColumn.Driver => "Driver",
        DriverTableColumn.LastPitStop => "Last pit",
        DriverTableColumn.TireCompound => "Tire",
        DriverTableColumn.IRating => "iRating",
        DriverTableColumn.IRatingDelta => "Δ",
        DriverTableColumn.License => "SR",
        DriverTableColumn.Lap => "Lap",
        DriverTableColumn.BestLap => "Best",
        DriverTableColumn.LastLap => "Last",
        _ => "Gap",
    };

    private static ChipSetting Column(string label, DriverTableColumn column, DriverTableOptions options, WidgetSettingsContext context) =>
        new(label, null, options.IsVisible(column), isVisible =>
        {
            options.SetVisible(column, isVisible);
            context.Persist(() => DriverTableOptionsStore.SaveColumn(options.Table, column, isVisible));
        });

    private static SettingsGroup StandingsTable(WidgetSettingsContext context) => new SettingsGroup("TABLE", "Which drivers to show, and what goes above and below the table.")
        .With(
            new NumberSetting(
                "Drivers around me",
                null,
                context.Standings.FocusSize,
                DriverTableOptions.MinFocusSize,
                60,
                1,
                "0",
                null,
                value => SetFocusSize(context.Standings, value, context)),
            new ToggleSetting(
                "Split by class",
                null,
                context.Standings.ShowMulticlass,
                value =>
                {
                    context.Standings.ShowMulticlass = value;
                    context.Persist(() => DriverTableOptionsStore.SaveMulticlass(DriverTable.Standings, value));
                }),
            new ToggleSetting(
                "Drivers per class",
                null,
                context.Standings.ShowClassDrivers,
                value =>
                {
                    context.Standings.ShowClassDrivers = value;
                    context.Persist(() => DriverTableOptionsStore.SaveClassDrivers(DriverTable.Standings, value));
                }),
            new ToggleSetting(
                "SOF per class",
                null,
                context.Standings.ShowClassSof,
                value =>
                {
                    context.Standings.ShowClassSof = value;
                    context.Persist(() => DriverTableOptionsStore.SaveClassSof(DriverTable.Standings, value));
                }),
            ColumnHeadersToggle(context.Standings, context))
        .With(InfoPlacements(context.Standings, context).ToArray());

    private static SettingsGroup RelativeTable(WidgetSettingsContext context) => new SettingsGroup("TABLE", "How many cars to show around you, and what goes above and below the table.")
        .With(
            new NumberSetting(
                "Drivers each side",
                null,
                context.Relative.FocusSize,
                DriverTableOptions.MinFocusSize,
                30,
                1,
                "0",
                null,
                value => SetFocusSize(context.Relative, value, context)),
            ColumnHeadersToggle(context.Relative, context))
        .With(InfoPlacements(context.Relative, context).ToArray());

    // Bound straight in the panels, so the row goes (and the widget shrinks) without a header nudge.
    private static ToggleSetting ColumnHeadersToggle(DriverTableOptions options, WidgetSettingsContext context) => new(
        "Show column headers",
        null,
        options.ShowColumnHeaders,
        value =>
        {
            options.ShowColumnHeaders = value;
            context.Persist(() => DriverTableOptionsStore.SaveColumnHeaders(options.Table, value));
        });

    private static readonly string[] SlotChoices =
        ["Hidden", "Top left", "Top center", "Top right", "Bottom left", "Bottom center", "Bottom right"];

    /// <summary>One dropdown per piece of session information: hidden, or the slot it sits in.</summary>
    private static IEnumerable<SettingItem> InfoPlacements(DriverTableOptions options, WidgetSettingsContext context) =>
        Enum.GetValues<TableInfoElement>().Select(element => (SettingItem)InfoPlacement(element, options, context));

    // "Hidden" keeps the slot, so showing it again puts it back where it was. The session type is
    // still saved under the switch that once named the class, so settings and layouts carry over.
    private static ChoiceSetting InfoPlacement(TableInfoElement element, DriverTableOptions options, WidgetSettingsContext context) => new(
        element switch
        {
            TableInfoElement.SessionType => "Session type",
            TableInfoElement.Sof => "SOF",
            TableInfoElement.SessionLaps => "Session laps",
            TableInfoElement.SessionTime => "Session time",
            TableInfoElement.BrakeBias => "Brake bias",
            TableInfoElement.AirTemp => "Air temp",
            TableInfoElement.TrackTemp => "Track temp",
            TableInfoElement.Incidents => "Incidents",
            _ => "Humidity",
        },
        null,
        SlotChoices,
        options.IsShown(element) ? (int)options.SlotOf(element) + 1 : 0,
        index =>
        {
            var shown = index > 0;
            options.SetShown(element, shown);
            if (shown)
            {
                options.SetSlot(element, (TableSlot)(index - 1));
            }

            context.Persist(() =>
            {
                DriverTableOptionsStore.SaveInfoShown(options.Table, element, shown);
                DriverTableOptionsStore.SaveInfoSlot(options.Table, element, options.SlotOf(element));
            });
            context.TableHeaderChanged?.Invoke(options.Table);
        });

    private static void SetFocusSize(DriverTableOptions options, double value, WidgetSettingsContext context)
    {
        options.FocusSize = (int)Math.Round(value);
        context.Persist(() => DriverTableOptionsStore.SaveFocusSize(options.Table, options.FocusSize));
    }

    // ===== Fuel calculator =====

    private static SettingsGroup FuelCalculatorBlocks(WidgetSettingsContext context)
    {
        var options = context.FuelCalculator;
        return new SettingsGroup("BLOCKS")
            .With(
                new ChipGroupSetting(
                    "Headline",
                    null,
                    [
                        FuelBlock("Laps left", options.ShowLapsRemaining, v => options.ShowLapsRemaining = v, context),
                        FuelBlock("Fuel bar", options.ShowFuelBar, v => options.ShowFuelBar = v, context),
                    ]),
                FuelCells("Usage", options.UsageOrder, order => options.UsageOrder = order, context),
                FuelCells("Strategy", options.StrategyOrder, order => options.StrategyOrder = order, context),
                new SegmentedSetting(
                    "First group",
                    "Auto puts usage first across and strategy first when vertical.",
                    ["Auto", "Usage", "Strategy"],
                    (int)options.FirstGroup,
                    index =>
                    {
                        options.FirstGroup = (FuelGroupOrder)index;
                        context.Persist(() => FuelCalculatorOptionsStore.Save(options));
                    }));
    }

    /// <summary>One group of cells as a drag list, each with its switch.</summary>
    private static ReorderListSetting FuelCells(string label, IReadOnlyList<FuelCell> order, Action<IReadOnlyList<FuelCell>> reorder, WidgetSettingsContext context)
    {
        var options = context.FuelCalculator;
        return new ReorderListSetting(
            label,
            "Drag a row by its handle to change the order.",
            order.Select(cell => new ReorderListItem(cell, FuelBlock(FuelCellLabel(cell), options.IsVisible(cell), v => options.SetVisible(cell, v), context))),
            newOrder =>
            {
                reorder(newOrder.Cast<FuelCell>().ToList());
                context.Persist(() => FuelCalculatorOptionsStore.Save(options));
            });
    }

    private static string FuelCellLabel(FuelCell cell) => cell switch
    {
        FuelCell.LastLap => "Last lap",
        FuelCell.Average => "Average",
        FuelCell.Minimum => "Minimum",
        FuelCell.Maximum => "Maximum",
        FuelCell.FuelRemaining => "Fuel left",
        FuelCell.FuelToFinish => "To finish",
        _ => "Refuel",
    };

    private static ChipSetting FuelBlock(string label, bool value, Action<bool> assign, WidgetSettingsContext context) =>
        new(label, null, value, isVisible =>
        {
            assign(isVisible);
            context.Persist(() => FuelCalculatorOptionsStore.Save(context.FuelCalculator));
        });

    private static SettingsGroup FuelCalculatorDisplay(WidgetSettingsContext context)
    {
        var options = context.FuelCalculator;
        return new SettingsGroup("DISPLAY")
            .With(new SegmentedSetting(
                "Orientation",
                null,
                ["Horizontal", "Vertical"],
                options.Vertical ? 1 : 0,
                index =>
                {
                    options.Vertical = index == 1;
                    context.Persist(() => FuelCalculatorOptionsStore.Save(options));
                }));
    }

    private static SettingsGroup FuelCalculatorMath(WidgetSettingsContext context)
    {
        var options = context.FuelCalculator;
        return new SettingsGroup("CALCULATION")
            .With(
                new ChoiceSetting(
                    "Average over",
                    "Recent laps react faster to fuel saving.",
                    ["Whole session", "Last 3 laps", "Last 5 laps", "Last 10 laps"],
                    (int)options.AverageSource,
                    index =>
                    {
                        options.AverageSource = (FuelAverageSource)index;
                        context.Persist(() => FuelCalculatorOptionsStore.Save(options));
                    }),
                new NumberSetting(
                    "Safety margin",
                    "Scales with consumption.",
                    options.MarginLaps, 0, 20, 0.5, "0.#", "laps",
                    value =>
                    {
                        options.MarginLaps = value;
                        context.Persist(() => FuelCalculatorOptionsStore.Save(options));
                    }),
                new NumberSetting(
                    "Extra reserve",
                    "A flat amount on top of the margin.",
                    Units.Volume(options.MarginLiters, Units.Current),
                    0,
                    Units.Current == UnitSystem.Imperial ? 13 : 50,
                    Units.Current == UnitSystem.Imperial ? 0.1 : 0.5,
                    "0.#",
                    Units.VolumeUnit(Units.Current),
                    value =>
                    {
                        // Stored in litres, like every fuel figure, whatever the display units.
                        options.MarginLiters = Units.VolumeToLiters(value, Units.Current);
                        context.Persist(() => FuelCalculatorOptionsStore.Save(options));
                    }));
    }

    // ===== Weather =====

    private static readonly string[] GraphicSizes = ["S", "M", "L"];

    private static SettingsGroup WeatherElements(WidgetSettingsContext context)
    {
        var options = context.Weather;
        return new SettingsGroup("ELEMENTS")
            .With(new ChipGroupSetting(
                "Visible elements",
                null,
                [
                    WeatherChip("Air temp", options.ShowAirTemp, v => options.ShowAirTemp = v, context),
                    WeatherChip("Track temp", options.ShowTrackTemp, v => options.ShowTrackTemp = v, context),
                    WeatherChip("Humidity", options.ShowHumidity, v => options.ShowHumidity = v, context),
                    WeatherChip("Wind speed", options.ShowWindSpeed, v => options.ShowWindSpeed = v, context),
                    WeatherChip("Wind arrow", options.ShowWindArrow, v => options.ShowWindArrow = v, context),
                    WeatherChip("Forecast icon", options.ShowForecast, v => options.ShowForecast = v, context),
                    WeatherChip("Rain chance", options.ShowRainProbability, v => options.ShowRainProbability = v, context),
                    WeatherChip("Track surface", options.ShowTrackWetness, v => options.ShowTrackWetness = v, context),
                ]));
    }

    private static ChipSetting WeatherChip(string label, bool value, Action<bool> assign, WidgetSettingsContext context) =>
        new(label, null, value, isVisible => SaveWeather(() => assign(isVisible), context));

    private static SettingsGroup WeatherDisplay(WidgetSettingsContext context)
    {
        var options = context.Weather;
        return new SettingsGroup("DISPLAY")
            .With(
                new ToggleSetting(
                    "Show labels",
                    "Captions such as AIR TEMP and WIND.",
                    options.ShowLabels,
                    value => SaveWeather(() => options.ShowLabels = value, context)),
                new SegmentedSetting(
                    "Orientation",
                    null,
                    ["Horizontal", "Vertical"],
                    options.Compact ? 0 : 1,
                    index => SaveWeather(() => options.Compact = index == 0, context)),
                new SegmentedSetting(
                    "Forecast icon size",
                    null,
                    GraphicSizes,
                    (int)options.IconSize,
                    index => SaveWeather(() => options.IconSize = (WeatherGraphicSize)index, context)),
                new SegmentedSetting(
                    "Wind compass size",
                    null,
                    GraphicSizes,
                    (int)options.ArrowSize,
                    index => SaveWeather(() => options.ArrowSize = (WeatherGraphicSize)index, context)),
                new SliderSetting(
                    "Wind compass refresh",
                    "10 Hz moves the arrow on each update; faster animates it and costs more GPU.",
                    options.CompassRefreshHz,
                    WeatherOptions.MinCompassRefreshHz,
                    WeatherOptions.MaxCompassRefreshHz,
                    WeatherOptions.CompassRefreshStepHz,
                    value => SaveWeather(() => options.CompassRefreshHz = (int)value, context),
                    value => $"{value:0} Hz"));
    }

    private static void SaveWeather(Action change, WidgetSettingsContext context)
    {
        change();
        context.Persist(() => WeatherOptionsStore.Save(context.Weather));
    }

    // ===== Track & session =====

    private static SettingsGroup TrackInfoElements(WidgetSettingsContext context)
    {
        var options = context.TrackInfo;
        return new SettingsGroup("ELEMENTS")
            .With(new ReorderListSetting(
                "Visible elements",
                "Drag a row by its handle to change the order.",
                options.FieldOrder.Select(field => new ReorderListItem(field, TrackInfoChip(field, options, context))),
                order =>
                {
                    options.FieldOrder = order.Cast<TrackInfoField>().ToList();
                    context.Persist(() => TrackInfoOptionsStore.Save(options));
                }));
    }

    private static string TrackInfoLabel(TrackInfoField field) => field switch
    {
        TrackInfoField.TrackName => "Track name",
        TrackInfoField.Session => "Session",
        TrackInfoField.AirTemp => "Air temp",
        TrackInfoField.TrackTemp => "Track temp",
        TrackInfoField.Wind => "Wind",
        TrackInfoField.Humidity => "Humidity",
        TrackInfoField.TrackUsage => "Track usage",
        TrackInfoField.TimeLeft => "Time left",
        _ => "Lap",
    };

    private static ChipSetting TrackInfoChip(TrackInfoField field, TrackInfoOptions options, WidgetSettingsContext context) =>
        new(TrackInfoLabel(field), null, options.IsVisible(field), isVisible =>
        {
            options.SetVisible(field, isVisible);
            context.Persist(() => TrackInfoOptionsStore.Save(options));
        });

    // ===== Cockpit =====

    // Index order matches CockpitTheme.
    private static readonly string[] CockpitThemeNames =
    [
        "Default", "GT Sports", "Casual", "Hypercar", "Pit Wall", "Classic Car", "Invisible",
    ];

    private static SettingsGroup CockpitThemeGroup(WidgetSettingsContext context) => new SettingsGroup("THEME")
        .With(new ChoiceSetting(
            "Cockpit theme",
            null,
            CockpitThemeNames,
            (int)context.Cockpit.Theme,
            index =>
            {
                context.Cockpit.Theme = (CockpitTheme)index;
                context.Persist(() => CockpitThemeStore.Save(context.Cockpit.Theme));
            }));

    // ===== Flags =====

    private static readonly (FlagGroup Group, string Label, string Hint)[] FlagGroupLabels =
    [
        (FlagGroup.Track, "Track status", "The most serious one shows."),
        (FlagGroup.Driver, "Aimed at you", "Penalties and warnings for your car."),
        (FlagGroup.Race, "Race progress", "Laps to go, halfway, last lap, finish."),
        (FlagGroup.Advisory, "Advisories", "Can stack with any other flag."),
    ];

    private static SettingsGroup FlagTypes(WidgetSettingsContext context)
    {
        var options = context.Flag;
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
                        options.IsEnabled(definition.Kind),
                        enabled => SaveFlags(() => options.SetEnabled(definition.Kind, enabled), context)))
                    .ToList()));
        }

        return group;
    }

    private static SettingsGroup FlagContent(WidgetSettingsContext context)
    {
        var options = context.Flag;
        return new SettingsGroup("CONTENT")
            .With(
                new ChoiceSetting(
                    "Show",
                    null,
                    ["Icon and text", "Icon only"],
                    (int)options.DisplayMode,
                    index => SaveFlags(() => options.DisplayMode = (FlagDisplayMode)index, context)),
                new ToggleSetting(
                    "Flag name",
                    null,
                    options.ShowName,
                    value => SaveFlags(() => options.ShowName = value, context)),
                new ToggleSetting(
                    "Event description",
                    "What the flag asks of you.",
                    options.ShowDescription,
                    value => SaveFlags(() => options.ShowDescription = value, context)),
                new NumberSetting(
                    "Flags at once",
                    "Most important first, and larger.",
                    options.MaxFlags,
                    1,
                    FlagOptions.MaxFlagsLimit,
                    1,
                    "0",
                    null,
                    value => SaveFlags(() => options.MaxFlags = (int)Math.Round(value), context)),
                new ChoiceSetting(
                    "Info flags stay up",
                    "Green, halfway and laps-to-go. Safety flags stay while out.",
                    FlagOptions.HoldChoices.Select(s => s == 0 ? "While active" : $"{s} seconds").ToList(),
                    Math.Max(0, FlagOptions.HoldChoices.ToList().IndexOf(options.InfoFlagSeconds)),
                    index => SaveFlags(() => options.InfoFlagSeconds = FlagOptions.HoldChoices[index], context)));
    }

    private static SettingsGroup FlagLayoutGroup(WidgetSettingsContext context)
    {
        var options = context.Flag;
        return new SettingsGroup("LAYOUT")
            .With(
                new ChoiceSetting(
                    "Stack flags",
                    null,
                    ["Vertically", "Side by side"],
                    (int)options.Layout,
                    index => SaveFlags(() => options.Layout = (FlagLayout)index, context)),
                new ChoiceSetting(
                    "Icon position",
                    null,
                    ["Left of the text", "Above the text"],
                    (int)options.IconPlacement,
                    index => SaveFlags(() => options.IconPlacement = (FlagIconPlacement)index, context)));
    }

    private static void SaveFlags(Action change, WidgetSettingsContext context)
    {
        change();
        context.Persist(() => FlagOptionsStore.Save(context.Flag));
    }

    // ===== Delta =====

    /// <summary>Never saved for the individual widget (it starts every run at the session best), so
    /// there is nothing to persist; a layout still keeps its own choice.</summary>
    private static SettingsGroup DeltaReferenceGroup(WidgetSettingsContext context) => new SettingsGroup("REFERENCE")
        .With(new ChoiceSetting(
            "Compare against",
            null,
            ["Session best lap", "Personal best (all-time)", "Optimal lap"],
            (int)context.Delta.Reference,
            index =>
            {
                context.Delta.Reference = (DeltaReference)index;
                context.Persist(() => { });
            }));

    // ===== Notes =====

    private static SettingsGroup HighRateNote() => new SettingsGroup(
        "UPDATE RATE",
        "Set in General › Performance.");
}
