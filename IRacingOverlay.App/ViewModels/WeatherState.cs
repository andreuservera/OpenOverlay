using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

/// <summary>Sky picture for the forecast icon. Night variants apply once the sun is below the horizon.</summary>
public enum WeatherCondition
{
    Unknown,
    Sunny,
    PartlyCloudy,
    Cloudy,
    Rain,
    NightClear,
    NightCloudy,
    NightRain,
}

/// <summary>
/// One tick of weather for the Weather widget. Every reading is nullable: a value the sim doesn't
/// report (or reports as garbage) shows as a dash rather than a confident-looking zero.
/// </summary>
public sealed class WeatherState
{
    public double? AirTempC { get; init; }
    public double? TrackTempC { get; init; }
    public double? HumidityPct { get; init; }
    public double? WindSpeedMs { get; init; }

    /// <summary>Where the wind comes from relative to the car's nose, degrees clockwise: 0 headwind,
    /// 90 from the right, 180 tailwind, 270 from the left.</summary>
    public double? WindFromRelativeDeg { get; init; }

    /// <summary>The car's heading, degrees clockwise from north — turns the dial's compass ring.</summary>
    public double? HeadingDeg { get; init; }

    public WeatherCondition Condition { get; init; }
    public double? RainChancePct { get; init; }

    /// <summary>iRacing's track wetness, 1 dry to 7 extremely wet; null when not reported.</summary>
    public int? TrackWetness { get; init; }

    public UnitSystem UnitSystem { get; init; }

    public static WeatherState Empty { get; } = new();

    public string AirTempDisplay => FormatTemperature(AirTempC);

    public string TrackTempDisplay => FormatTemperature(TrackTempC);

    public string TemperatureUnit => Units.TemperatureUnit(UnitSystem);

    public string WindSpeedDisplay => WindSpeedMs is { } ms
        ? Units.SpeedFromMs(ms, UnitSystem).ToString("0", CultureInfo.InvariantCulture)
        : "—";

    public string WindSpeedUnit => Units.SpeedUnit(UnitSystem);

    public string HumidityDisplay => HumidityPct is { } pct ? $"{pct.ToString("0", CultureInfo.InvariantCulture)}%" : "—";

    public string RainChanceDisplay => RainChancePct is { } pct ? $"{pct.ToString("0", CultureInfo.InvariantCulture)}%" : "—";

    /// <summary>Low / medium / high, for the green / yellow / red rain colour. Null when unknown.</summary>
    public RainRisk? RainRisk => RainChancePct switch
    {
        null => null,
        < 30 => ViewModels.RainRisk.Low,
        < 60 => ViewModels.RainRisk.Medium,
        _ => ViewModels.RainRisk.High,
    };

    public string TrackWetnessDisplay => TrackWetness switch
    {
        >= 1 and <= 7 => Loc.T($"weather.wetness.{TrackWetness}"),
        _ => "—",
    };

    /// <summary>Same three-colour scale as the rain chance: dry, damp, properly wet.</summary>
    public RainRisk? TrackWetnessRisk => TrackWetness switch
    {
        1 or 2 => ViewModels.RainRisk.Low,
        3 or 4 or 5 => ViewModels.RainRisk.Medium,
        6 or 7 => ViewModels.RainRisk.High,
        _ => null,
    };

    public string WindDirectionDescription => WindFromRelativeDeg switch
    {
        null => Loc.T("weather.wind.unavailable"),
        < 22.5 or >= 337.5 => Loc.F("weather.wind.head", CompassSuffix),
        < 67.5 => Loc.F("weather.wind.frontRight", CompassSuffix),
        < 112.5 => Loc.F("weather.wind.right", CompassSuffix),
        < 157.5 => Loc.F("weather.wind.rearRight", CompassSuffix),
        < 202.5 => Loc.F("weather.wind.tail", CompassSuffix),
        < 247.5 => Loc.F("weather.wind.rearLeft", CompassSuffix),
        < 292.5 => Loc.F("weather.wind.left", CompassSuffix),
        _ => Loc.F("weather.wind.frontLeft", CompassSuffix),
    };

    /// <summary>Compass point the wind blows from, e.g. "NW". Empty without a heading.</summary>
    public string WindFromCardinal => WindFromRelativeDeg is { } relative && HeadingDeg is { } heading
        ? Cardinal(relative + heading)
        : "";

    /// <summary>Compass point the wind blows towards.</summary>
    public string WindToCardinal => WindFromRelativeDeg is { } relative && HeadingDeg is { } heading
        ? Cardinal(relative + heading + 180)
        : "";

    private string CompassSuffix => WindFromCardinal.Length > 0 ? Loc.F("weather.wind.compass", WindFromCardinal, WindToCardinal) : "";

    private static readonly string[] CompassPoints = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    private static string Cardinal(double degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        return CompassPoints[(int)Math.Round(normalized / 45) % CompassPoints.Length];
    }

    public string ConditionDescription => Condition switch
    {
        WeatherCondition.Sunny => Loc.T("weather.sky.sunny"),
        WeatherCondition.PartlyCloudy => Loc.T("weather.sky.partlyCloudy"),
        WeatherCondition.Cloudy => Loc.T("weather.sky.cloudy"),
        WeatherCondition.Rain => Loc.T("weather.sky.rain"),
        WeatherCondition.NightClear => Loc.T("weather.sky.nightClear"),
        WeatherCondition.NightCloudy => Loc.T("weather.sky.nightCloudy"),
        WeatherCondition.NightRain => Loc.T("weather.sky.nightRain"),
        _ => Loc.T("weather.sky.unavailable"),
    };

    private string FormatTemperature(double? celsius) => celsius is { } c
        ? Units.Temperature(c, UnitSystem).ToString("0.0", CultureInfo.InvariantCulture)
        : "—";
}

public enum RainRisk
{
    Low,
    Medium,
    High,
}
