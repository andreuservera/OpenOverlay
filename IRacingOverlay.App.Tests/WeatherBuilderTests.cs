using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;
using Xunit;

namespace IRacingOverlay.App.Tests;

public class WeatherBuilderTests
{
    private static SyntheticMemoryBuilder WeatherVars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("AirTemp", IrsdkVarType.Float);
        builder.AddVar("TrackTempCrew", IrsdkVarType.Float);
        builder.AddVar("RelativeHumidity", IrsdkVarType.Float);
        builder.AddVar("WindVel", IrsdkVarType.Float);
        builder.AddVar("WindDir", IrsdkVarType.Float);
        builder.AddVar("YawNorth", IrsdkVarType.Float);
        builder.AddVar("Skies", IrsdkVarType.Int);
        builder.AddVar("Precipitation", IrsdkVarType.Float);
        builder.AddVar("SolarAltitude", IrsdkVarType.Float);
        builder.AddVar("WeatherDeclaredWet", IrsdkVarType.Bool);
        return builder;
    }

    private static WeatherState Build(Action<SyntheticMemoryBuilder.TickBufferWriter> write, string? rainChance = null) =>
        WeatherBuilder.Build(
            TestSnapshotFactory.Build(WeatherVars(), write),
            new IracingSessionInfo { WeekendInfo = new WeekendInfoSection { TrackPrecipitation = rainChance } });

    [Fact]
    public void Build_ReadsTemperaturesHumidityAndWind()
    {
        var state = Build(w =>
        {
            w.SetFloat("AirTemp", 21.4f);
            w.SetFloat("TrackTempCrew", 33.8f);
            w.SetFloat("RelativeHumidity", 0.54f);
            w.SetFloat("WindVel", 3.6f);
        });

        Assert.Equal("21.4", state.AirTempDisplay);
        Assert.Equal("°C", state.TemperatureUnit);
        Assert.Equal("54%", state.HumidityDisplay);
        Assert.Equal("13", state.WindSpeedDisplay);
        Assert.Equal("km/h", state.WindSpeedUnit);
    }

    [Fact]
    public void Build_FollowsIRacingDisplayUnits()
    {
        var builder = WeatherVars();
        builder.AddVar("DisplayUnits", IrsdkVarType.Int);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("DisplayUnits", 0); // English
            w.SetFloat("TrackTempCrew", 33.8f);
            w.SetFloat("WindVel", 3.6f);
        });

        var state = WeatherBuilder.Build(snapshot, null);

        Assert.Equal(UnitSystem.Imperial, state.UnitSystem);
        Assert.Equal("92.8", state.TrackTempDisplay);
        Assert.Equal("°F", state.TemperatureUnit);
        Assert.Equal("8", state.WindSpeedDisplay);
        Assert.Equal("mph", state.WindSpeedUnit);
    }

    [Theory]
    [InlineData(0, 0, 0)]      // heading north, wind from north: headwind
    [InlineData(0, 180, 180)]  // wind from behind: tailwind
    [InlineData(90, 180, 90)]  // heading east, wind from south: from the right
    [InlineData(90, 0, 270)]   // heading east, wind from north: from the left
    public void Build_WindIsRelativeToTheCarsHeading(double yawDeg, double windDeg, double expected)
    {
        var state = Build(w =>
        {
            w.SetFloat("YawNorth", (float)(yawDeg * Math.PI / 180));
            w.SetFloat("WindDir", (float)(windDeg * Math.PI / 180));
        });

        Assert.Equal(expected, state.WindFromRelativeDeg!.Value, 3);
        Assert.Equal(yawDeg, state.HeadingDeg!.Value, 3);
    }

    [Fact]
    public void Build_WindCompassPointsAreAbsolute()
    {
        // Heading east with the wind from the north-west: it blows towards the south-east.
        var state = Build(w =>
        {
            w.SetFloat("YawNorth", (float)(Math.PI / 2));
            w.SetFloat("WindDir", (float)(315 * Math.PI / 180));
        });

        Assert.Equal("NW", state.WindFromCardinal);
        Assert.Equal("SE", state.WindToCardinal);
    }

    [Theory]
    [InlineData(0, 0.5f, WeatherCondition.Sunny)]
    [InlineData(1, 0.5f, WeatherCondition.PartlyCloudy)]
    [InlineData(3, 0.5f, WeatherCondition.Cloudy)]
    [InlineData(0, -0.2f, WeatherCondition.NightClear)]
    [InlineData(2, -0.2f, WeatherCondition.NightCloudy)]
    public void Build_MapsSkiesAndSunToCondition(int skies, float solarAltitude, WeatherCondition expected)
    {
        var state = Build(w =>
        {
            w.SetInt("Skies", skies);
            w.SetFloat("SolarAltitude", solarAltitude);
        });

        Assert.Equal(expected, state.Condition);
    }

    [Fact]
    public void Build_RainingOverridesSkyAndReadsAsCertain()
    {
        var state = Build(w =>
        {
            w.SetInt("Skies", 0);
            w.SetFloat("SolarAltitude", -0.3f);
            w.SetFloat("Precipitation", 0.4f);
        }, rainChance: "25 %");

        Assert.Equal(WeatherCondition.NightRain, state.Condition);
        Assert.Equal(100, state.RainChancePct);
        Assert.Equal(RainRisk.High, state.RainRisk);
    }

    [Fact]
    public void Build_RainChanceComesFromTheSession()
    {
        var state = Build(w => w.SetFloat("SolarAltitude", 0.5f), rainChance: "15 %");

        Assert.Equal("15%", state.RainChanceDisplay);
        Assert.Equal(RainRisk.Low, state.RainRisk);
    }

    [Fact]
    public void Build_MissingVariables_FallBackToDashes()
    {
        var state = WeatherBuilder.Build(TestSnapshotFactory.Build(new SyntheticMemoryBuilder(), _ => { }), null);

        Assert.Equal("—", state.AirTempDisplay);
        Assert.Equal("—", state.WindSpeedDisplay);
        Assert.Null(state.WindFromRelativeDeg);
        Assert.Equal(WeatherCondition.Unknown, state.Condition);
        Assert.Equal("—", state.RainChanceDisplay);
    }

    [Fact]
    public void Options_SectionsAndDividersCloseUpAroundHiddenElements()
    {
        var options = new WeatherOptions { ShowWindArrow = false, ShowWindSpeed = false, ShowHumidity = false };

        Assert.False(options.ShowWindSection);
        Assert.True(options.ShowFirstDivider);
        Assert.False(options.ShowSecondDivider);

        options.ShowAirTemp = false;
        options.ShowTrackTemp = false;
        Assert.False(options.ShowFirstDivider);
    }
}
