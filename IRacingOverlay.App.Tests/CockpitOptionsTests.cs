using System.Text.Json.Nodes;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.ViewModels;
using static IRacingOverlay.App.ViewModels.CockpitModule;

namespace IRacingOverlay.App.Tests;

public class CockpitOptionsTests
{
    private readonly List<CockpitOptions> _saved = [];

    [Fact]
    public void Defaults_AreThePitWallTable_WithLightsAndRadar_AndLaterModulesHidden()
    {
        var options = new CockpitOptions();

        Assert.Equal([Gear, Speed, Rpm, Abs, Fuel, Inputs, WaterTemp, OilTemp], options.VisibleModules());
        Assert.True(options.ShowShiftLights);
        Assert.True(options.ShowProximityRadar);
        Assert.All([BrakeBias, TractionControl, Incidents, Delta], module => Assert.False(options.IsVisible(module)));
    }

    [Fact]
    public void Load_AppendsModulesTheSavedOrderDoesNotName_Hidden()
    {
        var options = new CockpitOptions();

        options.Load([Speed, Gear], [Speed, Gear], showShiftLights: false, showProximityRadar: true);

        Assert.Equal([Speed, Gear, Rpm, Abs, Fuel, Inputs, WaterTemp, OilTemp, BrakeBias, TractionControl, Incidents, Delta], options.ModuleOrder);
        Assert.Equal([Speed, Gear], options.VisibleModules());
        Assert.False(options.ShowShiftLights);
    }

    [Fact]
    public void Codec_RoundTripsTheConfiguration()
    {
        var source = new CockpitOptions();
        source.Load([Rpm, Gear, Fuel, Speed], [Rpm, Fuel, Speed], showShiftLights: false, showProximityRadar: false);
        var config = Codec(source).Read();

        var target = new CockpitOptions();
        var codec = Codec(target);
        codec.Apply(config);

        Assert.Equal(source.ModuleOrder, target.ModuleOrder);
        Assert.Equal([Rpm, Fuel, Speed], target.VisibleModules());
        Assert.False(target.ShowShiftLights);
        Assert.False(target.ShowProximityRadar);
        Assert.True(JsonNode.DeepEquals(config, codec.Read()));
        Assert.Single(_saved);
    }

    [Theory]
    [InlineData("""{"theme":"PitWall"}""")]
    [InlineData("""{"moduleOrder":"Gear"}""")]
    [InlineData("""{}""")]
    public void Codec_LoadsTheDefaults_ForAThemeOrInvalidConfig(string json)
    {
        var options = new CockpitOptions();
        options.Load([Rpm], [Rpm], showShiftLights: false, showProximityRadar: false);

        Codec(options).Apply((JsonObject)JsonNode.Parse(json)!);

        Assert.Equal(CockpitOptions.DefaultVisible, options.VisibleModules());
        Assert.True(options.ShowShiftLights);
        Assert.True(options.ShowProximityRadar);
    }

    [Fact]
    public void Codec_DropsUnknownModules_AndAppendsMissingOnesHidden()
    {
        var options = new CockpitOptions();

        Codec(options).Apply((JsonObject)JsonNode.Parse("""
            {
              "moduleOrder": ["Turbo", "Speed", "Gear", 42],
              "modules": { "Turbo": true, "Speed": true, "Gear": true },
              "showShiftLights": true,
              "showProximityRadar": "yes"
            }
            """)!);

        Assert.Equal([Speed, Gear], options.VisibleModules());
        Assert.Equal([Speed, Gear, Rpm, Abs, Fuel, Inputs, WaterTemp, OilTemp, BrakeBias, TractionControl, Incidents, Delta], options.ModuleOrder);
        Assert.True(options.ShowProximityRadar);
    }

    private IWidgetConfigCodec Codec(CockpitOptions options) => new CockpitConfigCodec(options, _saved.Add);
}
