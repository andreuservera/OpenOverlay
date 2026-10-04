using System.Globalization;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// The car and weather readings a driver table can show around itself: brake bias, air and track
/// temperature, humidity and the player's incidents. Each is null when the sim doesn't report it (brake bias on a car
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
    /// <summary>The player's incidents (the team's in a team race) and the limit; null when not reported.</summary>
    public IncidentState? Incidents { get; init; }

    public static TableConditions Empty { get; } = new();

    /// <summary>The bias as the black box shows it, to one decimal, as the front share in percent.</summary>
    public string BrakeBiasDisplay => BrakeBias is { } bias ? $"{bias.ToString("0.0", CultureInfo.InvariantCulture)}%" : "—";

    public string AirTempDisplay => Temperature(AirTempC);

    public string TrackTempDisplay => Temperature(TrackTempC);

    public string HumidityDisplay => HumidityPct is { } pct ? $"{pct.ToString("0", CultureInfo.InvariantCulture)}%" : "—";

    /// <summary>"7/17" against the limit, a bare "7" when the session has none.</summary>
    public string IncidentsDisplay => Incidents is not { } incidents
        ? "—"
        : incidents.Limit is { } limit
            ? string.Create(CultureInfo.InvariantCulture, $"{incidents.CountedTotal}/{limit}")
            : incidents.CountedTotal.ToString(CultureInfo.InvariantCulture);

    public IncidentSeverity IncidentSeverity => Incidents?.Severity ?? IncidentSeverity.Normal;

    public static TableConditions Build(TelemetrySnapshot telemetry, IracingSessionInfo? session = null) => new()
    {
        BrakeBias = Read(telemetry, TelemetryVarNames.BrakeBias, -100, 100),
        AirTempC = Read(telemetry, TelemetryVarNames.AirTemp, -60, 70),
        TrackTempC = Read(telemetry, TelemetryVarNames.TrackTempCrew, -60, 100),
        // iRacing's "%" unit is a 0-1 fraction.
        HumidityPct = Read(telemetry, TelemetryVarNames.RelativeHumidity, 0, 1) * 100,
        Condition = WeatherBuilder.Condition(telemetry),
        UnitSystem = Units.Read(telemetry),
        Incidents = IncidentBuilder.Build(telemetry, session) is var incidents && !ReferenceEquals(incidents, IncidentState.Empty)
            ? incidents
            : null,
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
