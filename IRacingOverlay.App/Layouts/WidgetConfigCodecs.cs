using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Layouts;

/// <summary>
/// Moves one widget type's own options in and out of a layout. <see cref="Read"/> captures the
/// options the individual widget has right now; <see cref="Apply"/> puts a captured set back, with
/// the same result as the user changing those options on the widget's page — the shared options
/// object updated, the choice saved, and the same notifications raised.
///
/// Only what the widget's page offers and persists belongs here. Size, opacity and auto-hide are
/// fields of <see cref="LayoutWidget"/> in their own right; application-wide settings (units, refresh
/// rates) and the flag preview stay out.
/// </summary>
public interface IWidgetConfigCodec
{
    /// <summary>The <see cref="WidgetCatalog"/> key.</summary>
    string Type { get; }

    /// <summary>Config keys that must never leave the machine (tokens, keys, credentials). None of
    /// today's widgets has any; export filters on this so a widget that gains one is covered.</summary>
    IReadOnlySet<string> SensitiveFields { get; }

    JsonObject Read();

    /// <summary>Keys that are missing, of the wrong type or out of the option's range are ignored,
    /// leaving that option as it is: a config written by another version is still applied as far as
    /// it can be. Nothing is saved or raised when nothing changes.</summary>
    void Apply(JsonObject config);
}

/// <summary>Where each widget's options are saved. <see cref="Stores"/> is the app's own; tests
/// substitute their own so they never write the user's settings.</summary>
public sealed record WidgetConfigPersistence(
    Action<DriverTableOptions> DriverTable,
    Action<FlagOptions> Flag,
    Action<CockpitTheme> Cockpit,
    Action<WeatherOptions> Weather,
    Action<FuelCalculatorOptions> FuelCalculator,
    Action<DeltaOptions> Delta)
{
    public static WidgetConfigPersistence Stores { get; } = new(
        DriverTableOptionsStore.Save,
        FlagOptionsStore.Save,
        CockpitThemeStore.Save,
        WeatherOptionsStore.Save,
        FuelCalculatorOptionsStore.Save,
        // The Delta reference is deliberately never saved for the individual widget.
        _ => { });
}

/// <summary>The live options objects the codecs read and write — the same instances the widgets,
/// the preview and the telemetry loop use — and how to save them and announce header changes.</summary>
public sealed record WidgetConfigTargets(
    DriverTableOptions Standings,
    DriverTableOptions Relative,
    FlagOptions Flag,
    CockpitOptions Cockpit,
    WeatherOptions Weather,
    FuelCalculatorOptions FuelCalculator,
    DeltaOptions Delta,
    WidgetConfigPersistence Persist,
    Action<DriverTable> TableHeaderChanged);

public static class WidgetConfigCodecs
{
    /// <summary>One codec per catalog entry. A widget with no options of its own still gets one,
    /// with an empty config, so every layout entry goes through the same path.</summary>
    public static IReadOnlyDictionary<string, IWidgetConfigCodec> Create(WidgetConfigTargets targets)
    {
        IWidgetConfigCodec[] codecs =
        [
            new DriverTableConfigCodec(WidgetCatalog.Standings, targets.Standings, targets.Persist.DriverTable, targets.TableHeaderChanged),
            new DriverTableConfigCodec(WidgetCatalog.Relative, targets.Relative, targets.Persist.DriverTable, targets.TableHeaderChanged),
            new FlagConfigCodec(targets.Flag, targets.Persist.Flag),
            new CockpitConfigCodec(targets.Cockpit, targets.Persist.Cockpit),
            new WeatherConfigCodec(targets.Weather, targets.Persist.Weather),
            new FuelCalculatorConfigCodec(targets.FuelCalculator, targets.Persist.FuelCalculator),
            new DeltaConfigCodec(targets.Delta, targets.Persist.Delta),
            new EmptyConfigCodec(WidgetCatalog.TireInfo),
            new EmptyConfigCodec(WidgetCatalog.PedalTrace),
            new EmptyConfigCodec(WidgetCatalog.Incident),
            new EmptyConfigCodec(WidgetCatalog.TrackInfo),
            new EmptyConfigCodec(WidgetCatalog.TrackMap),
        ];

        return codecs.ToDictionary(codec => codec.Type);
    }
}

/// <summary>Shared plumbing: typed reads out of a config, and change tracking so a no-op Apply
/// neither saves nor notifies.</summary>
internal abstract class WidgetConfigCodec : IWidgetConfigCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly IReadOnlySet<string> NoSensitiveFields = new HashSet<string>();

    protected WidgetConfigCodec(string type) => Type = type;

    public string Type { get; }

    public virtual IReadOnlySet<string> SensitiveFields => NoSensitiveFields;

    public abstract JsonObject Read();

    /// <summary>Assigns what the config holds, then saves and notifies only if the options read
    /// back differently. Compared on what <see cref="Read"/> returns rather than on property-change
    /// events, which some options raise for derived properties even when nothing changed.</summary>
    public void Apply(JsonObject config)
    {
        var before = Read();
        Assign(config);
        var after = Read();
        if (!JsonNode.DeepEquals(before, after))
        {
            OnChanged(before, after);
        }
    }

    protected abstract void Assign(JsonObject config);

    /// <summary>Saves the options, and raises whatever the widget's page raises for the same change.</summary>
    protected abstract void OnChanged(JsonObject before, JsonObject after);

    protected static bool Changed(JsonObject before, JsonObject after, string key) =>
        !JsonNode.DeepEquals(before[key], after[key]);

    protected static void Set<T>(JsonObject? config, string key, Action<T> assign)
    {
        if (TryGet<T>(config, key, out var value))
        {
            assign(value);
        }
    }

    protected static string Name<T>(T value) where T : struct, Enum => value.ToString();

    private static bool TryGet<T>(JsonObject? config, string key, out T value)
    {
        value = default!;
        if (config?[key] is not JsonValue node)
        {
            return false;
        }

        try
        {
            var parsed = node.Deserialize<T>(JsonOptions);
            if (parsed is null || (parsed is Enum && !Enum.IsDefined(typeof(T), parsed)))
            {
                return false;
            }

            value = parsed;
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            return false;
        }
    }
}

internal sealed class EmptyConfigCodec(string type) : WidgetConfigCodec(type)
{
    public override JsonObject Read() => [];

    protected override void Assign(JsonObject config)
    {
    }

    protected override void OnChanged(JsonObject before, JsonObject after)
    {
    }
}

/// <summary>Standings and Relative: the options their pages offer. "Split by class" is Standings
/// only — Relative's page doesn't offer it and its table ignores it.</summary>
internal sealed class DriverTableConfigCodec(
    string type,
    DriverTableOptions options,
    Action<DriverTableOptions> persist,
    Action<DriverTable> headerChanged) : WidgetConfigCodec(type)
{
    private bool HasMulticlass => options.Table == DriverTable.Standings;

    public override JsonObject Read()
    {
        var columns = new JsonObject();
        foreach (var column in Enum.GetValues<DriverTableColumn>())
        {
            columns[Name(column)] = options.IsVisible(column);
        }

        var config = new JsonObject
        {
            ["columns"] = columns,
            ["focusSize"] = options.FocusSize,
            ["showClassName"] = options.ShowClassName,
            ["showSessionId"] = options.ShowSessionId,
            ["showSessionLaps"] = options.ShowSessionLaps,
            ["showSessionTime"] = options.ShowSessionTime,
        };
        if (HasMulticlass)
        {
            config["showMulticlass"] = options.ShowMulticlass;
        }

        return config;
    }

    protected override void Assign(JsonObject config)
    {
        var columns = config["columns"] as JsonObject;
        foreach (var column in Enum.GetValues<DriverTableColumn>())
        {
            Set<bool>(columns, Name(column), visible => options.SetVisible(column, visible));
        }

        Set<int>(config, "focusSize", value => options.FocusSize = value);
        Set<bool>(config, "showClassName", value => options.ShowClassName = value);
        Set<bool>(config, "showSessionId", value => options.ShowSessionId = value);
        Set<bool>(config, "showSessionLaps", value => options.ShowSessionLaps = value);
        Set<bool>(config, "showSessionTime", value => options.ShowSessionTime = value);
        if (HasMulticlass)
        {
            Set<bool>(config, "showMulticlass", value => options.ShowMulticlass = value);
        }
    }

    protected override void OnChanged(JsonObject before, JsonObject after)
    {
        persist(options);
        // The same nudge the page gives: the header fields are otherwise only pushed on the
        // standings tick, leaving a stale value on screen for up to a second.
        if (Changed(before, after, "showClassName") || Changed(before, after, "showSessionId"))
        {
            headerChanged(options.Table);
        }
    }
}

/// <summary>Every flag type's on/off state is captured, not just the ones the user changed, so a
/// layout means the same thing whatever defaults the individual widget had.</summary>
internal sealed class FlagConfigCodec(FlagOptions options, Action<FlagOptions> persist) : WidgetConfigCodec(WidgetCatalog.Flag)
{
    public override JsonObject Read()
    {
        var enabled = new JsonObject();
        foreach (var definition in FlagCatalog.All)
        {
            enabled[Name(definition.Kind)] = options.IsEnabled(definition.Kind);
        }

        return new JsonObject
        {
            ["enabled"] = enabled,
            ["displayMode"] = Name(options.DisplayMode),
            ["showName"] = options.ShowName,
            ["showDescription"] = options.ShowDescription,
            ["maxFlags"] = options.MaxFlags,
            ["infoFlagSeconds"] = options.InfoFlagSeconds,
            ["layout"] = Name(options.Layout),
            ["iconPlacement"] = Name(options.IconPlacement),
        };
    }

    protected override void Assign(JsonObject config)
    {
        var enabled = config["enabled"] as JsonObject;
        foreach (var definition in FlagCatalog.All)
        {
            Set<bool>(enabled, Name(definition.Kind), value => options.SetEnabled(definition.Kind, value));
        }

        Set<FlagDisplayMode>(config, "displayMode", value => options.DisplayMode = value);
        Set<bool>(config, "showName", value => options.ShowName = value);
        Set<bool>(config, "showDescription", value => options.ShowDescription = value);
        Set<int>(config, "maxFlags", value => options.MaxFlags = value);
        Set<int>(config, "infoFlagSeconds", value => options.InfoFlagSeconds = value);
        Set<FlagLayout>(config, "layout", value => options.Layout = value);
        Set<FlagIconPlacement>(config, "iconPlacement", value => options.IconPlacement = value);
    }

    protected override void OnChanged(JsonObject before, JsonObject after) => persist(options);
}

internal sealed class CockpitConfigCodec(CockpitOptions options, Action<CockpitTheme> persist) : WidgetConfigCodec(WidgetCatalog.Cockpit)
{
    public override JsonObject Read() => new() { ["theme"] = Name(options.Theme) };

    protected override void Assign(JsonObject config) => Set<CockpitTheme>(config, "theme", value => options.Theme = value);

    protected override void OnChanged(JsonObject before, JsonObject after) => persist(options.Theme);
}

internal sealed class WeatherConfigCodec(WeatherOptions options, Action<WeatherOptions> persist) : WidgetConfigCodec(WidgetCatalog.Weather)
{
    public override JsonObject Read() => new()
    {
        ["showAirTemp"] = options.ShowAirTemp,
        ["showTrackTemp"] = options.ShowTrackTemp,
        ["showHumidity"] = options.ShowHumidity,
        ["showWindSpeed"] = options.ShowWindSpeed,
        ["showWindArrow"] = options.ShowWindArrow,
        ["showForecast"] = options.ShowForecast,
        ["showRainProbability"] = options.ShowRainProbability,
        ["showTrackWetness"] = options.ShowTrackWetness,
        ["showLabels"] = options.ShowLabels,
        ["compact"] = options.Compact,
        ["iconSize"] = Name(options.IconSize),
        ["arrowSize"] = Name(options.ArrowSize),
        ["compassRefreshHz"] = options.CompassRefreshHz,
    };

    protected override void Assign(JsonObject config)
    {
        Set<bool>(config, "showAirTemp", value => options.ShowAirTemp = value);
        Set<bool>(config, "showTrackTemp", value => options.ShowTrackTemp = value);
        Set<bool>(config, "showHumidity", value => options.ShowHumidity = value);
        Set<bool>(config, "showWindSpeed", value => options.ShowWindSpeed = value);
        Set<bool>(config, "showWindArrow", value => options.ShowWindArrow = value);
        Set<bool>(config, "showForecast", value => options.ShowForecast = value);
        Set<bool>(config, "showRainProbability", value => options.ShowRainProbability = value);
        Set<bool>(config, "showTrackWetness", value => options.ShowTrackWetness = value);
        Set<bool>(config, "showLabels", value => options.ShowLabels = value);
        Set<bool>(config, "compact", value => options.Compact = value);
        Set<WeatherGraphicSize>(config, "iconSize", value => options.IconSize = value);
        Set<WeatherGraphicSize>(config, "arrowSize", value => options.ArrowSize = value);
        Set<int>(config, "compassRefreshHz", value => options.CompassRefreshHz = value);
    }

    protected override void OnChanged(JsonObject before, JsonObject after) => persist(options);
}

/// <summary>The extra reserve is kept in litres whatever the display units, as the store keeps it.</summary>
internal sealed class FuelCalculatorConfigCodec(FuelCalculatorOptions options, Action<FuelCalculatorOptions> persist)
    : WidgetConfigCodec(WidgetCatalog.FuelCalculator)
{
    public override JsonObject Read() => new()
    {
        ["showFuelBar"] = options.ShowFuelBar,
        ["showFuelRemaining"] = options.ShowFuelRemaining,
        ["showLastLap"] = options.ShowLastLap,
        ["showAverage"] = options.ShowAverage,
        ["showMinimum"] = options.ShowMinimum,
        ["showMaximum"] = options.ShowMaximum,
        ["showLapsRemaining"] = options.ShowLapsRemaining,
        ["showFuelToFinish"] = options.ShowFuelToFinish,
        ["showRefuel"] = options.ShowRefuel,
        ["averageSource"] = Name(options.AverageSource),
        ["marginLaps"] = options.MarginLaps,
        ["marginLiters"] = options.MarginLiters,
        ["vertical"] = options.Vertical,
    };

    protected override void Assign(JsonObject config)
    {
        Set<bool>(config, "showFuelBar", value => options.ShowFuelBar = value);
        Set<bool>(config, "showFuelRemaining", value => options.ShowFuelRemaining = value);
        Set<bool>(config, "showLastLap", value => options.ShowLastLap = value);
        Set<bool>(config, "showAverage", value => options.ShowAverage = value);
        Set<bool>(config, "showMinimum", value => options.ShowMinimum = value);
        Set<bool>(config, "showMaximum", value => options.ShowMaximum = value);
        Set<bool>(config, "showLapsRemaining", value => options.ShowLapsRemaining = value);
        Set<bool>(config, "showFuelToFinish", value => options.ShowFuelToFinish = value);
        Set<bool>(config, "showRefuel", value => options.ShowRefuel = value);
        Set<FuelAverageSource>(config, "averageSource", value => options.AverageSource = value);
        Set<double>(config, "marginLaps", value => options.MarginLaps = value);
        Set<double>(config, "marginLiters", value => options.MarginLiters = value);
        Set<bool>(config, "vertical", value => options.Vertical = value);
    }

    protected override void OnChanged(JsonObject before, JsonObject after) => persist(options);
}

/// <summary>Which lap the Delta measures against. Not saved for the individual widget, but a layout
/// keeps its own choice and applies it while open.</summary>
internal sealed class DeltaConfigCodec(DeltaOptions options, Action<DeltaOptions> persist) : WidgetConfigCodec(WidgetCatalog.Delta)
{
    public override JsonObject Read() => new() { ["reference"] = Name(options.Reference) };

    protected override void Assign(JsonObject config) => Set<DeltaReference>(config, "reference", value => options.Reference = value);

    protected override void OnChanged(JsonObject before, JsonObject after) => persist(options);
}
