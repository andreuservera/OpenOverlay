using System.Globalization;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// The car and weather readings a driver table can show around itself: brake bias, air and track
/// temperature and humidity. Each is null when the sim doesn't report it (brake bias on a car
/// without an adjuster), and shows as a dash rather than a confident-looking zero.
/// </summary>
public sealed class TableConditions
{
    public double? BrakeBias { get; init; }
    public double? AirTempC { get; init; }
    public double? TrackTempC { get; init; }
    public double? HumidityPct { get; init; }
    public WeatherCondition Condition { get; init; }
    public UnitSystem UnitSystem { get; init; }

    public static TableConditions Empty { get; } = new();

    /// <summary>The bias as the black box shows it, to one decimal, as the front share in percent.</summary>
    public string BrakeBiasDisplay => BrakeBias is { } bias ? $"{bias.ToString("0.0", CultureInfo.InvariantCulture)}%" : "—";

    public string AirTempDisplay => Temperature(AirTempC);

    public string TrackTempDisplay => Temperature(TrackTempC);

    public string HumidityDisplay => HumidityPct is { } pct ? $"{pct.ToString("0", CultureInfo.InvariantCulture)}%" : "—";

    public static TableConditions Build(TelemetrySnapshot telemetry) => new()
    {
        BrakeBias = Read(telemetry, TelemetryVarNames.BrakeBias, -100, 100),
        AirTempC = Read(telemetry, TelemetryVarNames.AirTemp, -60, 70),
        TrackTempC = Read(telemetry, TelemetryVarNames.TrackTempCrew, -60, 100),
        // iRacing's "%" unit is a 0-1 fraction.
        HumidityPct = Read(telemetry, TelemetryVarNames.RelativeHumidity, 0, 1) * 100,
        Condition = WeatherBuilder.Condition(telemetry),
        UnitSystem = Units.Read(telemetry),
    };

    private string Temperature(double? celsius) => celsius is { } c
        ? $"{Units.Temperature(c, UnitSystem).ToString("0.#", CultureInfo.InvariantCulture)}{Units.TemperatureUnit(UnitSystem)}"
        : "—";

    /// <summary>A float reading, or null when the variable is missing or outside a plausible range.</summary>
    private static double? Read(TelemetrySnapshot telemetry, string name, double min, double max)
    {
        if (!telemetry.HasVariable(name))
        {
            return null;
        }

        var value = (double)telemetry.GetFloat(name);
        return double.IsFinite(value) && value >= min && value <= max ? value : null;
    }
}
