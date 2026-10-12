using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets.Cockpit;

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
    PedalTraceOptions PedalTrace,
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
/// here is what does not belong to a layout: POSITION &amp; VISIBILITY (size, opacity, auto-hide),
/// which the page binds to the widget's slot and the editor to the layout entry, and the flag preview
/// scenario.
///
/// Every widget sorts its options into the same sections, in the same order — <see cref="Content"/>,
/// <see cref="Appearance"/>, <see cref="Behavior"/>, then (on the page) position and visibility, and
/// <see cref="Advanced"/> last — so a user who has learnt one widget's page has learnt them all.
/// </summary>
public static class WidgetSettings
{
    /// <summary>What the widget shows, and in what order.</summary>
    public static string Content => Loc.T("ws.group.content");

    /// <summary>How it looks: orientation, labels, sizes of its parts.</summary>
    public static string Appearance => Loc.T("ws.group.appearance");

    /// <summary>What it decides: counts, references, calculations.</summary>
    public static string Behavior => Loc.T("ws.group.behavior");

    /// <summary>Size, opacity and when it shows; added by the page, not here.</summary>
    public static string PositionAndVisibility => Loc.T("ws.group.positionAndVisibility");

    /// <summary>Technical options most people never need, folded until opened.</summary>
    public static string Advanced => Loc.T("ws.group.advanced");

    /// <summary>The widget's own settings groups, in the order its page shows them. Empty for a
    /// widget with no options of its own.</summary>
    public static IReadOnlyList<SettingsGroup> For(string type, WidgetSettingsContext context) => type switch
    {
        WidgetCatalog.Standings => [StandingsContent(context), TableAppearance(context.Standings, context), StandingsBehavior(context)],
        WidgetCatalog.Relative => [RelativeContent(context), TableAppearance(context.Relative, context), RelativeBehavior(context)],
        WidgetCatalog.FuelCalculator => [FuelCalculatorBlocks(context), FuelCalculatorDisplay(context), FuelCalculatorMath(context)],
        WidgetCatalog.Flag => [FlagTypes(context), FlagAppearance(context), FlagBehavior(context)],
        WidgetCatalog.Cockpit => [CockpitModulesGroup(context)],
        WidgetCatalog.PedalTrace => [PedalTraceElements(context)],
        WidgetCatalog.Weather => [WeatherElements(context), WeatherDisplay(context), WeatherAdvanced(context)],
        WidgetCatalog.TrackInfo => [TrackInfoElements(context)],
        WidgetCatalog.Delta => [DeltaReferenceGroup(context)],
        _ => [],
    };

    /// <summary>Options as they come, for the lists' "Reset order".</summary>
    private static class Defaults
    {
        public static FuelCalculatorOptions FuelCalculator { get; } = new();
        public static PedalTraceOptions PedalTrace { get; } = new();
    }

    // ===== Driver tables =====

    private static SettingsGroup StandingsContent(WidgetSettingsContext context) => new SettingsGroup(
        Content, Loc.T("ws.theColumnsAndThe"))
        .With(
            ColumnList(context.Standings, context),
            new ToggleSetting(
                Loc.T("ws.driversPerClass"),
                null,
                context.Standings.ShowClassDrivers,
                value =>
                {
                    context.Standings.ShowClassDrivers = value;
                    context.Persist(() => DriverTableOptionsStore.SaveClassDrivers(DriverTable.Standings, value));
                }),
            new ToggleSetting(
                Loc.T("ws.sofPerClass"),
                Loc.T("ws.strengthOfFieldThe"),
                context.Standings.ShowClassSof,
                value =>
                {
                    context.Standings.ShowClassSof = value;
                    context.Persist(() => DriverTableOptionsStore.SaveClassSof(DriverTable.Standings, value));
                }),
            InfoBar(context.Standings, context));

    private static SettingsGroup RelativeContent(WidgetSettingsContext context) => new SettingsGroup(
        Content, Loc.T("ws.theColumnsAndThe"))
        .With(ColumnList(context.Relative, context), InfoBar(context.Relative, context));

    private static ReorderListSetting ColumnList(DriverTableOptions options, WidgetSettingsContext context) => new(
        Loc.T("ws.columns"),
        Loc.T("ws.dragARowBy"),
        options.ColumnOrder.Select(column => column == DriverTableColumn.IRating
            ? new ReorderListItem(
                column,
                Column("iRating", DriverTableColumn.IRating, options, context),
                Column(Loc.T("ws.gainLoss"), DriverTableColumn.IRatingDelta, options, context))
            : new ReorderListItem(column, Column(ColumnLabel(column), column, options, context))),
        order =>
        {
            options.ColumnOrder = order.Cast<DriverTableColumn>().ToList();
            context.Persist(() => DriverTableOptionsStore.SaveColumnOrder(options.Table, options.ColumnOrder));
        },
        DriverTableColumnLayout.DefaultOrder.Cast<Enum>());

    private static string ColumnLabel(DriverTableColumn column) => column switch
    {
        DriverTableColumn.Position => Loc.T("ws.pos"),
        DriverTableColumn.PlacesGained => Loc.T("ws.positionsGainedLost"),
        DriverTableColumn.CarNumber => Loc.T("ws.car"),
        DriverTableColumn.CarBrand => Loc.T("ws.carMakeLogo"),
        DriverTableColumn.CountryFlag => Loc.T("ws.flag"),
        DriverTableColumn.Driver => Loc.T("ws.driver"),
        DriverTableColumn.LastPitStop => Loc.T("ws.lastPit"),
        DriverTableColumn.TireCompound => Loc.T("ws.tire"),
        DriverTableColumn.IRating => "iRating",
        DriverTableColumn.IRatingDelta => Loc.T("ws.gainLoss"),
        DriverTableColumn.License => Loc.T("ws.srSafetyRating"),
        DriverTableColumn.Lap => Loc.T("ws.lap"),
        DriverTableColumn.BestLap => Loc.T("ws.best"),
        DriverTableColumn.LastLap => Loc.T("ws.last"),
        _ => Loc.T("ws.gap"),
    };

    private static ChipSetting Column(string label, DriverTableColumn column, DriverTableOptions options, WidgetSettingsContext context) =>
        new(label, null, options.IsVisible(column), isVisible =>
        {
            options.SetVisible(column, isVisible);
            context.Persist(() => DriverTableOptionsStore.SaveColumn(options.Table, column, isVisible));
        });

    private static SettingsGroup StandingsBehavior(WidgetSettingsContext context) => new SettingsGroup(Behavior)
        .With(
            new NumberSetting(
                Loc.T("ws.driversAroundMe"),
                Loc.T("ws.rowsKeptAroundYour"),
                context.Standings.FocusSize,
                DriverTableOptions.MinFocusSize,
                60,
                1,
                "0",
                null,
                value => SetFocusSize(context.Standings, value, context)),
            new ToggleSetting(
                Loc.T("ws.splitByClass"),
                Loc.T("ws.inMulticlassRacesOne"),
                context.Standings.ShowMulticlass,
                value =>
                {
                    context.Standings.ShowMulticlass = value;
                    context.Persist(() => DriverTableOptionsStore.SaveMulticlass(DriverTable.Standings, value));
                }));

    private static SettingsGroup RelativeBehavior(WidgetSettingsContext context) => new SettingsGroup(Behavior)
        .With(new NumberSetting(
            Loc.T("ws.driversEachSide"),
            Loc.T("ws.carsShownAheadOf"),
            context.Relative.FocusSize,
            DriverTableOptions.MinFocusSize,
            30,
            1,
            "0",
            null,
            value => SetFocusSize(context.Relative, value, context)));

    private static SettingsGroup TableAppearance(DriverTableOptions options, WidgetSettingsContext context) =>
        new SettingsGroup(Appearance).With(ColumnHeadersToggle(options, context));

    // Bound straight in the panels, so the row goes (and the widget shrinks) without a header nudge.
    private static ToggleSetting ColumnHeadersToggle(DriverTableOptions options, WidgetSettingsContext context) => new(
        Loc.T("ws.showColumnHeaders"),
        null,
        options.ShowColumnHeaders,
        value =>
        {
            options.ShowColumnHeaders = value;
            context.Persist(() => DriverTableOptionsStore.SaveColumnHeaders(options.Table, value));
        });

    /// <summary>The session info around the table, as a picture of the table with slots to drop it in.</summary>
    private static InfoBarSetting InfoBar(DriverTableOptions options, WidgetSettingsContext context) => new(
        Loc.T("ws.infoBar"),
        Loc.T("ws.dragInfoIntoA"),
        Enum.GetValues<TableInfoElement>().Select(element => element switch
        {
            TableInfoElement.SessionType => new InfoChip(element, Loc.T("ws.session"), Loc.T("ws.practiceQualifyingOrRace")),
            TableInfoElement.Sof => new InfoChip(element, "SOF", Loc.T("ws.strengthOfFieldThe2")),
            TableInfoElement.SessionLaps => new InfoChip(element, Loc.T("ws.laps"), Loc.T("ws.lapsDoneAndLaps")),
            TableInfoElement.SessionTime => new InfoChip(element, Loc.T("ws.time"), Loc.T("ws.sessionTimeRunAnd")),
            TableInfoElement.BrakeBias => new InfoChip(element, Loc.T("ws.brakeBias"), Loc.T("ws.yourCarSBrake")),
            TableInfoElement.AirTemp => new InfoChip(element, Loc.T("ws.airTemp"), Loc.T("ws.airTemperature")),
            TableInfoElement.TrackTemp => new InfoChip(element, Loc.T("ws.trackTemp"), Loc.T("ws.trackSurfaceTemperature")),
            TableInfoElement.Incidents => new InfoChip(element, Loc.T("ws.incidents"), Loc.T("ws.yourIncidentCountAnd")),
            _ => new InfoChip(element, Loc.T("ws.humidity"), Loc.T("ws.relativeHumidity")),
        }).ToList(),
        options.IsShown,
        options.SlotOf,
        // A chip taken off the table keeps its slot, so showing it again puts it back where it was.
        // The session type is still saved under the switch that once named the class, so settings
        // and layouts carry over.
        (element, shown, slot) =>
        {
            options.SetShown(element, shown);
            options.SetSlot(element, slot);
            context.Persist(() =>
            {
                DriverTableOptionsStore.SaveInfoShown(options.Table, element, shown);
                DriverTableOptionsStore.SaveInfoSlot(options.Table, element, slot);
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
        return new SettingsGroup(Content, Loc.T("ws.theBlocksItShows"))
            .With(
                new ChipGroupSetting(
                    Loc.T("ws.headline"),
                    null,
                    [
                        FuelBlock(Loc.T("ws.lapsLeft"), options.ShowLapsRemaining, v => options.ShowLapsRemaining = v, context),
                        FuelBlock(Loc.T("ws.fuelBar"), options.ShowFuelBar, v => options.ShowFuelBar = v, context),
                    ]),
                FuelCells(Loc.T("ws.usage"), options.UsageOrder, Defaults.FuelCalculator.UsageOrder, order => options.UsageOrder = order, context),
                FuelCells(Loc.T("ws.strategy"), options.StrategyOrder, Defaults.FuelCalculator.StrategyOrder, order => options.StrategyOrder = order, context));
    }

    /// <summary>One group of cells as a drag list, each with its switch.</summary>
    private static ReorderListSetting FuelCells(string label, IReadOnlyList<FuelCell> order, IReadOnlyList<FuelCell> defaultOrder, Action<IReadOnlyList<FuelCell>> reorder, WidgetSettingsContext context)
    {
        var options = context.FuelCalculator;
        return new ReorderListSetting(
            label,
            Loc.T("ws.dragARowBy"),
            order.Select(cell => new ReorderListItem(cell, FuelBlock(FuelCellLabel(cell), options.IsVisible(cell), v => options.SetVisible(cell, v), context))),
            newOrder =>
            {
                reorder(newOrder.Cast<FuelCell>().ToList());
                context.Persist(() => FuelCalculatorOptionsStore.Save(options));
            },
            defaultOrder.Cast<Enum>());
    }

    private static string FuelCellLabel(FuelCell cell) => cell switch
    {
        FuelCell.LastLap => Loc.T("ws.lastLap"),
        FuelCell.Average => Loc.T("ws.average"),
        FuelCell.Minimum => Loc.T("ws.minimum"),
        FuelCell.Maximum => Loc.T("ws.maximum"),
        FuelCell.FuelRemaining => Loc.T("ws.fuelLeft"),
        FuelCell.FuelToFinish => Loc.T("ws.toFinish"),
        _ => Loc.T("ws.refuel"),
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
        return new SettingsGroup(Appearance)
            .With(
                new SegmentedSetting(
                    Loc.T("ws.orientation"),
                    null,
                    [Loc.T("ws.horizontal"), Loc.T("ws.vertical")],
                    options.Vertical ? 1 : 0,
                    index =>
                    {
                        options.Vertical = index == 1;
                        context.Persist(() => FuelCalculatorOptionsStore.Save(options));
                    }),
                new SegmentedSetting(
                    Loc.T("ws.firstGroup"),
                    Loc.T("ws.autoPutsUsageFirst"),
                    [Loc.T("ws.auto"), Loc.T("ws.usage"), Loc.T("ws.strategy")],
                    (int)options.FirstGroup,
                    index =>
                    {
                        options.FirstGroup = (FuelGroupOrder)index;
                        context.Persist(() => FuelCalculatorOptionsStore.Save(options));
                    }));
    }

    private static SettingsGroup FuelCalculatorMath(WidgetSettingsContext context)
    {
        var options = context.FuelCalculator;
        return new SettingsGroup(Behavior, Loc.T("ws.howTheFuelFigures"))
            .With(
                new ChoiceSetting(
                    Loc.T("ws.averageOver"),
                    Loc.T("ws.recentLapsReactFaster"),
                    [Loc.T("ws.wholeSession"), Loc.T("ws.last3Laps"), Loc.T("ws.last5Laps"), Loc.T("ws.last10Laps")],
                    (int)options.AverageSource,
                    index =>
                    {
                        options.AverageSource = (FuelAverageSource)index;
                        context.Persist(() => FuelCalculatorOptionsStore.Save(options));
                    }),
                new NumberSetting(
                    Loc.T("ws.safetyMargin"),
                    Loc.T("ws.scalesWithConsumption"),
                    options.MarginLaps, 0, 20, 0.5, "0.#", Loc.T("ws.laps2"),
                    value =>
                    {
                        options.MarginLaps = value;
                        context.Persist(() => FuelCalculatorOptionsStore.Save(options));
                    }),
                new NumberSetting(
                    Loc.T("ws.extraReserve"),
                    Loc.T("ws.aFlatAmountOn"),
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
        return new SettingsGroup(Content)
            .With(new ChipGroupSetting(
                Loc.T("ws.items"),
                null,
                [
                    WeatherChip(Loc.T("ws.airTemp"), options.ShowAirTemp, v => options.ShowAirTemp = v, context),
                    WeatherChip(Loc.T("ws.trackTemp"), options.ShowTrackTemp, v => options.ShowTrackTemp = v, context),
                    WeatherChip(Loc.T("ws.humidity"), options.ShowHumidity, v => options.ShowHumidity = v, context),
                    WeatherChip(Loc.T("ws.windSpeed"), options.ShowWindSpeed, v => options.ShowWindSpeed = v, context),
                    WeatherChip(Loc.T("ws.windArrow"), options.ShowWindArrow, v => options.ShowWindArrow = v, context),
                    WeatherChip(Loc.T("ws.forecastIcon"), options.ShowForecast, v => options.ShowForecast = v, context),
                    WeatherChip(Loc.T("ws.rainChance"), options.ShowRainProbability, v => options.ShowRainProbability = v, context),
                    WeatherChip(Loc.T("ws.trackSurface"), options.ShowTrackWetness, v => options.ShowTrackWetness = v, context),
                ]));
    }

    private static ChipSetting WeatherChip(string label, bool value, Action<bool> assign, WidgetSettingsContext context) =>
        new(label, null, value, isVisible => SaveWeather(() => assign(isVisible), context));

    private static SettingsGroup WeatherDisplay(WidgetSettingsContext context)
    {
        var options = context.Weather;
        return new SettingsGroup(Appearance)
            .With(
                new ToggleSetting(
                    Loc.T("ws.showLabels"),
                    Loc.T("ws.captionsSuchAsAir"),
                    options.ShowLabels,
                    value => SaveWeather(() => options.ShowLabels = value, context)),
                new SegmentedSetting(
                    Loc.T("ws.orientation"),
                    null,
                    [Loc.T("ws.horizontal"), Loc.T("ws.vertical")],
                    options.Compact ? 0 : 1,
                    index => SaveWeather(() => options.Compact = index == 0, context)),
                new SegmentedSetting(
                    Loc.T("ws.forecastIconSize"),
                    null,
                    GraphicSizes,
                    (int)options.IconSize,
                    index => SaveWeather(() => options.IconSize = (WeatherGraphicSize)index, context)),
                new SegmentedSetting(
                    Loc.T("ws.windCompassSize"),
                    null,
                    GraphicSizes,
                    (int)options.ArrowSize,
                    index => SaveWeather(() => options.ArrowSize = (WeatherGraphicSize)index, context)));
    }

    private static SettingsGroup WeatherAdvanced(WidgetSettingsContext context)
    {
        var options = context.Weather;
        return new SettingsGroup(Advanced) { CollapsedByDefault = true }
            .With(
                new SliderSetting(
                    Loc.T("ws.windCompassRefresh"),
                    Loc.T("ws.10HzMovesThe"),
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
        return new SettingsGroup(Content)
            .With(new ReorderListSetting(
                Loc.T("ws.items"),
                Loc.T("ws.dragARowBy"),
                options.FieldOrder.Select(field => new ReorderListItem(field, TrackInfoChip(field, options, context))),
                order =>
                {
                    options.FieldOrder = order.Cast<TrackInfoField>().ToList();
                    context.Persist(() => TrackInfoOptionsStore.Save(options));
                },
                TrackInfoOptions.DefaultOrder.Cast<Enum>()));
    }

    private static string TrackInfoLabel(TrackInfoField field) => field switch
    {
        TrackInfoField.TrackName => Loc.T("ws.trackName"),
        TrackInfoField.Session => Loc.T("ws.session"),
        TrackInfoField.AirTemp => Loc.T("ws.airTemp"),
        TrackInfoField.TrackTemp => Loc.T("ws.trackTemp"),
        TrackInfoField.Wind => Loc.T("ws.wind"),
        TrackInfoField.Humidity => Loc.T("ws.humidity"),
        TrackInfoField.TrackUsage => Loc.T("ws.trackUsage"),
        TrackInfoField.TimeLeft => Loc.T("ws.timeLeft"),
        _ => Loc.T("ws.lap"),
    };

    private static ChipSetting TrackInfoChip(TrackInfoField field, TrackInfoOptions options, WidgetSettingsContext context) =>
        new(TrackInfoLabel(field), null, options.IsVisible(field), isVisible =>
        {
            options.SetVisible(field, isVisible);
            context.Persist(() => TrackInfoOptionsStore.Save(options));
        });

    // ===== Pedal trace =====

    private static SettingsGroup PedalTraceElements(WidgetSettingsContext context)
    {
        var options = context.PedalTrace;
        return new SettingsGroup(Content)
            .With(new ReorderListSetting(
                Loc.T("ws.items"),
                Loc.T("ws.dragARowBy"),
                options.ElementOrder.Select(element => new ReorderListItem(element, PedalTraceChip(element, options, context))),
                order =>
                {
                    options.ElementOrder = order.Cast<PedalTraceElement>().ToList();
                    context.Persist(() => PedalTraceOptionsStore.Save(options));
                },
                Defaults.PedalTrace.ElementOrder.Cast<Enum>()));
    }

    private static string PedalTraceLabel(PedalTraceElement element) => element switch
    {
        PedalTraceElement.Gear => Loc.T("ws.gear"),
        PedalTraceElement.Speed => Loc.T("ws.speed"),
        PedalTraceElement.Steering => Loc.T("ws.steeringWheel"),
        PedalTraceElement.Trace => Loc.T("ws.pedalTrace"),
        _ => Loc.T("ws.pedalBars"),
    };

    private static ChipSetting PedalTraceChip(PedalTraceElement element, PedalTraceOptions options, WidgetSettingsContext context) =>
        new(PedalTraceLabel(element), null, options.IsVisible(element), isVisible =>
        {
            options.SetVisible(element, isVisible);
            context.Persist(() => PedalTraceOptionsStore.Save(options));
        });

    // ===== Cockpit =====

    private static SettingsGroup CockpitModulesGroup(WidgetSettingsContext context)
    {
        var options = context.Cockpit;
        return new SettingsGroup(Content)
            .With(
                new ReorderListSetting(
                    Loc.T("ws.items"),
                    Loc.T("ws.dragARowBy2"),
                    options.ModuleOrder.Select(module => new ReorderListItem(module, CockpitModuleChip(module, options, context))),
                    order =>
                    {
                        options.ModuleOrder = order.Cast<CockpitModule>().ToList();
                        context.Persist(() => CockpitOptionsStore.Save(options));
                    },
                    CockpitOptions.DefaultOrder.Cast<Enum>()),
                new ToggleSetting(Loc.T("ws.shiftLights"), null, options.ShowShiftLights, value =>
                {
                    options.ShowShiftLights = value;
                    context.Persist(() => CockpitOptionsStore.Save(options));
                }),
                new ToggleSetting(Loc.T("ws.proximityRadar"), null, options.ShowProximityRadar, value =>
                {
                    options.ShowProximityRadar = value;
                    context.Persist(() => CockpitOptionsStore.Save(options));
                }));
    }

    private static ChipSetting CockpitModuleChip(CockpitModule module, CockpitOptions options, WidgetSettingsContext context) =>
        new(CockpitModules.Of(module).Name, null, options.IsVisible(module), isVisible =>
        {
            options.SetVisible(module, isVisible);
            context.Persist(() => CockpitOptionsStore.Save(options));
        });

    // ===== Flags =====

    private static (FlagGroup Group, string Label, string Hint)[] FlagGroupLabels =>
    [
        (FlagGroup.Track, Loc.T("ws.trackStatus"), Loc.T("ws.theMostSeriousOne")),
        (FlagGroup.Driver, Loc.T("ws.aimedAtYou"), Loc.T("ws.penaltiesAndWarningsFor")),
        (FlagGroup.Race, Loc.T("ws.raceProgress"), Loc.T("ws.lapsToGoHalfway")),
        (FlagGroup.Advisory, Loc.T("ws.advisories"), Loc.T("ws.canStackWithAny")),
    ];

    private static SettingsGroup FlagTypes(WidgetSettingsContext context)
    {
        var options = context.Flag;
        var group = new SettingsGroup(
            Content,
            Loc.T("ws.whichFlagsCanAppear"));

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

    private static SettingsGroup FlagAppearance(WidgetSettingsContext context)
    {
        var options = context.Flag;
        return new SettingsGroup(Appearance)
            .With(
                new SegmentedSetting(
                    Loc.T("ws.show"),
                    null,
                    [Loc.T("ws.iconAndText"), Loc.T("ws.iconOnly")],
                    (int)options.DisplayMode,
                    index => SaveFlags(() => options.DisplayMode = (FlagDisplayMode)index, context)),
                new ToggleSetting(
                    Loc.T("ws.flagName"),
                    null,
                    options.ShowName,
                    value => SaveFlags(() => options.ShowName = value, context)),
                new ToggleSetting(
                    Loc.T("ws.eventDescription"),
                    Loc.T("ws.whatTheFlagAsks"),
                    options.ShowDescription,
                    value => SaveFlags(() => options.ShowDescription = value, context)),
                new SegmentedSetting(
                    Loc.T("ws.stackFlags"),
                    null,
                    [Loc.T("ws.vertically"), Loc.T("ws.sideBySide")],
                    (int)options.Layout,
                    index => SaveFlags(() => options.Layout = (FlagLayout)index, context)),
                new SegmentedSetting(
                    Loc.T("ws.iconPosition"),
                    null,
                    [Loc.T("ws.leftOfText"), Loc.T("ws.aboveText")],
                    (int)options.IconPlacement,
                    index => SaveFlags(() => options.IconPlacement = (FlagIconPlacement)index, context)));
    }

    private static SettingsGroup FlagBehavior(WidgetSettingsContext context)
    {
        var options = context.Flag;
        return new SettingsGroup(Behavior)
            .With(
                new NumberSetting(
                    Loc.T("ws.flagsAtOnce"),
                    Loc.T("ws.mostImportantFirstAnd"),
                    options.MaxFlags,
                    1,
                    FlagOptions.MaxFlagsLimit,
                    1,
                    "0",
                    null,
                    value => SaveFlags(() => options.MaxFlags = (int)Math.Round(value), context)),
                new ChoiceSetting(
                    Loc.T("ws.infoFlagsStayUp"),
                    Loc.T("ws.greenHalfwayAndLaps"),
                    FlagOptions.HoldChoices.Select(s => s == 0 ? Loc.T("ws.whileActive") : Loc.F("ws.flags.holdSeconds", s)).ToList(),
                    Math.Max(0, FlagOptions.HoldChoices.ToList().IndexOf(options.InfoFlagSeconds)),
                    index => SaveFlags(() => options.InfoFlagSeconds = FlagOptions.HoldChoices[index], context)));
    }

    private static void SaveFlags(Action change, WidgetSettingsContext context)
    {
        change();
        context.Persist(() => FlagOptionsStore.Save(context.Flag));
    }

    // ===== Delta =====

    /// <summary>Never saved for the individual widget (it starts every run at the session best), so
    /// there is nothing to persist; a layout still keeps its own choice.</summary>
    private static SettingsGroup DeltaReferenceGroup(WidgetSettingsContext context) => new SettingsGroup(Behavior)
        .With(new SegmentedSetting(
            Loc.T("ws.compareAgainst"),
            null,
            [Loc.T("ws.sessionBest"), Loc.T("ws.personalBest"), Loc.T("ws.optimalLap")],
            (int)context.Delta.Reference,
            index =>
            {
                context.Delta.Reference = (DeltaReference)index;
                context.Persist(() => { });
            }));
}
