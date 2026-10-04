using System.Text.Json.Nodes;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public sealed class WidgetConfigCodecTests
{
    // Enum-valued config keys, so a test can move each to a different valid value.
    private static readonly Dictionary<string, Type> EnumKeys = new()
    {
        ["displayMode"] = typeof(FlagDisplayMode),
        ["layout"] = typeof(FlagLayout),
        ["iconPlacement"] = typeof(FlagIconPlacement),
        ["theme"] = typeof(CockpitTheme),
        ["iconSize"] = typeof(WeatherGraphicSize),
        ["arrowSize"] = typeof(WeatherGraphicSize),
        ["averageSource"] = typeof(FuelAverageSource),
        ["firstGroup"] = typeof(FuelGroupOrder),
        ["reference"] = typeof(DeltaReference),
        ["sessionTypeSlot"] = typeof(TableSlot),
        ["sofSlot"] = typeof(TableSlot),
        ["sessionLapsSlot"] = typeof(TableSlot),
        ["sessionTimeSlot"] = typeof(TableSlot),
        ["brakeBiasSlot"] = typeof(TableSlot),
        ["airTempSlot"] = typeof(TableSlot),
        ["trackTempSlot"] = typeof(TableSlot),
        ["humiditySlot"] = typeof(TableSlot),
    };

    // Integer keys that only take multiples of a step, so a test moves them by that step.
    private static readonly Dictionary<string, int> SteppedKeys = new()
    {
        ["compassRefreshHz"] = WeatherOptions.CompassRefreshStepHz,
    };

    private readonly List<string> _saved = [];
    private readonly List<DriverTable> _headerChanges = [];

    public static TheoryData<string> CatalogTypes()
    {
        var data = new TheoryData<string>();
        foreach (var descriptor in WidgetCatalog.All)
        {
            data.Add(descriptor.Key);
        }

        return data;
    }

    [Fact]
    public void EveryCatalogType_HasACodec()
    {
        var codecs = NewCodecs();

        Assert.Equal(
            WidgetCatalog.All.Select(descriptor => descriptor.Key).Order(),
            codecs.Keys.Order());
        Assert.All(codecs, pair => Assert.Equal(pair.Key, pair.Value.Type));
    }

    [Theory]
    [MemberData(nameof(CatalogTypes))]
    public void NoWidget_DeclaresSensitiveFieldsToday(string type)
    {
        Assert.Empty(NewCodecs()[type].SensitiveFields);
    }

    [Theory]
    [MemberData(nameof(CatalogTypes))]
    public void ApplyingWhatWasRead_ChangesNothing_SavesNothing_AndRaisesNothing(string type)
    {
        var codec = NewCodecs()[type];
        var before = codec.Read();

        codec.Apply((JsonObject)before.DeepClone());

        Assert.True(JsonNode.DeepEquals(before, codec.Read()));
        Assert.Empty(_saved);
        Assert.Empty(_headerChanges);
    }

    [Theory]
    [MemberData(nameof(CatalogTypes))]
    public void ApplyingAModifiedConfig_ThenReading_ReturnsThatConfig_SavedOnce(string type)
    {
        var codec = NewCodecs()[type];
        var modified = (JsonObject)codec.Read().DeepClone();
        Modify(modified);

        codec.Apply(modified);

        Assert.True(JsonNode.DeepEquals(modified, codec.Read()), codec.Read().ToJsonString());
        Assert.Equal(modified.Count == 0 ? 0 : 1, _saved.Count);
    }

    [Theory]
    [InlineData(WidgetCatalog.Standings, DriverTable.Standings)]
    [InlineData(WidgetCatalog.Relative, DriverTable.Relative)]
    public void HeaderFieldChange_RaisesTableHeaderChanged_LikeThePageDoes(string type, DriverTable table)
    {
        var codec = NewCodecs()[type];
        var config = codec.Read();
        config["showSof"] = !(bool)config["showSof"]!;

        codec.Apply(config);

        Assert.Equal([table], _headerChanges);
    }

    [Fact]
    public void ColumnOnlyChange_SavesButDoesNotRaiseTableHeaderChanged()
    {
        var codec = NewCodecs()[WidgetCatalog.Relative];
        var config = codec.Read();
        config["columns"]!["IRating"] = false;

        codec.Apply(config);

        Assert.Single(_saved);
        Assert.Empty(_headerChanges);
        Assert.False((bool)codec.Read()["columns"]!["IRating"]!);
    }

    [Fact]
    public void Apply_IgnoresMissingWrongTypedAndOutOfRangeValues()
    {
        var codec = NewCodecs()[WidgetCatalog.Flag];
        var before = codec.Read();

        codec.Apply(new JsonObject
        {
            ["displayMode"] = "Hologram",
            ["showName"] = "yes",
            ["layout"] = 42,
            ["unknownKey"] = true,
        });

        Assert.True(JsonNode.DeepEquals(before, codec.Read()));
        Assert.Empty(_saved);
    }

    [Fact]
    public void Relative_DoesNotCarryTheStandingsOnlyClassSplit()
    {
        var codecs = NewCodecs();

        Assert.True(codecs[WidgetCatalog.Standings].Read().ContainsKey("showMulticlass"));
        Assert.False(codecs[WidgetCatalog.Relative].Read().ContainsKey("showMulticlass"));
    }

    private IReadOnlyDictionary<string, IWidgetConfigCodec> NewCodecs() =>
        WidgetConfigCodecs.Create(new WidgetConfigTargets(
            new DriverTableOptions(DriverTable.Standings),
            new DriverTableOptions(DriverTable.Relative),
            new FlagOptions(),
            new CockpitOptions(),
            new WeatherOptions(),
            new TrackInfoOptions(),
            new FuelCalculatorOptions(),
            new DeltaOptions(),
            new WidgetConfigPersistence(
                options => _saved.Add($"DriverTable.{options.Table}"),
                _ => _saved.Add("Flag"),
                _ => _saved.Add("Cockpit"),
                _ => _saved.Add("Weather"),
                _ => _saved.Add("TrackInfo"),
                _ => _saved.Add("FuelCalculator"),
                _ => _saved.Add("Delta")),
            _headerChanges.Add));

    /// <summary>Moves every value to a different valid one: booleans flip, numbers go up, enums
    /// move to their next value.</summary>
    private static void Modify(JsonObject config)
    {
        foreach (var (key, node) in config.ToList())
        {
            config[key] = node switch
            {
                JsonObject nested => Modified(nested),
                // A list (the column order) changes to its reverse: still complete and valid.
                JsonArray list => new JsonArray(list.Reverse().Select(node => node?.DeepClone()).ToArray()),
                JsonValue value when value.TryGetValue<bool>(out var flag) => !flag,
                JsonValue value when value.TryGetValue<int>(out var number) => number + SteppedKeys.GetValueOrDefault(key, 1),
                JsonValue value when value.TryGetValue<double>(out var number) => number + 0.5,
                JsonValue value when value.TryGetValue<string>(out var name) => NextName(EnumKeys[key], name),
                _ => throw new InvalidOperationException($"Unexpected config value at {key}"),
            };
        }
    }

    private static JsonObject Modified(JsonObject nested)
    {
        var copy = (JsonObject)nested.DeepClone();
        Modify(copy);
        return copy;
    }

    private static string NextName(Type enumType, string current)
    {
        var names = Enum.GetNames(enumType);
        return names[(Array.IndexOf(names, current) + 1) % names.Length];
    }
}
