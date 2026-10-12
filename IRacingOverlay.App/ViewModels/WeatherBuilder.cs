using System.Globalization;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Reads the Weather widget's state from live telemetry. iRacing's SDK exposes no forecast, so the
/// forecast icon is the sky the sim reports right now (cloud cover, precipitation, sun below the
/// horizon), and the rain probability is the event's configured chance of rain from the session
/// info — shown as 100% while it is actually raining.
/// </summary>
internal static class WeatherBuilder
{
    private const double RainingThreshold = 0.01;

    public static WeatherState Build(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        var raining = IsRaining(telemetry);
        var chance = ParsePercent(session?.WeekendInfo?.TrackPrecipitation);
        if (raining)
        {
            chance = 100;
        }

        return new WeatherState
        {
            AirTempC = Read(telemetry, TelemetryVarNames.AirTemp, -60, 70),
            TrackTempC = Read(telemetry, TelemetryVarNames.TrackTempCrew, -60, 100),
            HumidityPct = Read(telemetry, TelemetryVarNames.RelativeHumidity, 0, 1) * 100,
            WindSpeedMs = Read(telemetry, TelemetryVarNames.WindVel, 0, 100),
            WindFromRelativeDeg = RelativeWind(telemetry),
            HeadingDeg = Heading(telemetry),
            Condition = ConditionOf(telemetry, raining),
            RainChancePct = chance,
            TrackWetness = telemetry.HasVariable(TelemetryVarNames.TrackWetness) &&
                telemetry.GetInt(TelemetryVarNames.TrackWetness) is var wetness and >= 1 and <= 7
                    ? wetness
                    : null,
            UnitSystem = Units.Read(telemetry),
        };
    }

    /// <summary>The sky the sim reports right now, as the forecast icon draws it. Also used by the
    /// Track &amp; session bar, so the two widgets always show the same picture.</summary>
    internal static WeatherCondition Condition(TelemetrySnapshot telemetry) => ConditionOf(telemetry, IsRaining(telemetry));

    /// <summary>Rain falling now. Not WeatherDeclaredWet: that is race control's wet-tyre call and
    /// stays on while a soaked track dries out — seen live at Spielberg with no rain falling and
    /// iRacing's own weather reading 0%, where it put the chance at 100% and drew a rain icon.</summary>
    private static bool IsRaining(TelemetrySnapshot telemetry) =>
        Read(telemetry, TelemetryVarNames.Precipitation, 0, 1) > RainingThreshold;

    /// <summary>WindDir is where the wind blows from, clockwise from north; subtracting the car's own
    /// heading turns it into "from the nose".</summary>
    private static double? RelativeWind(TelemetrySnapshot telemetry)
    {
        if (Read(telemetry, TelemetryVarNames.WindDir, -100, 100) is not { } windDir ||
            Read(telemetry, TelemetryVarNames.YawNorth, -100, 100) is not { } yaw)
        {
            return null;
        }

        return Normalize((windDir - yaw) * (180 / Math.PI));
    }

    private static double? Heading(TelemetrySnapshot telemetry) =>
        Read(telemetry, TelemetryVarNames.YawNorth, -100, 100) is { } yaw ? Normalize(yaw * (180 / Math.PI)) : null;

    private static double Normalize(double degrees)
    {
        degrees %= 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static WeatherCondition ConditionOf(TelemetrySnapshot telemetry, bool raining)
    {
        var night = Read(telemetry, TelemetryVarNames.SolarAltitude, -Math.PI, Math.PI) < 0;
        if (raining)
        {
            return night ? WeatherCondition.NightRain : WeatherCondition.Rain;
        }

        if (!telemetry.HasVariable(TelemetryVarNames.Skies))
        {
            return WeatherCondition.Unknown;
        }

        return (telemetry.GetInt(TelemetryVarNames.Skies), night) switch
        {
            (0, false) => WeatherCondition.Sunny,
            (0, true) => WeatherCondition.NightClear,
            (1, false) => WeatherCondition.PartlyCloudy,
            (2 or 3, false) => WeatherCondition.Cloudy,
            (1 or 2 or 3, true) => WeatherCondition.NightCloudy,
            _ => WeatherCondition.Unknown,
        };
    }

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

    /// <summary>"15 %" → 15. iRacing writes the unit after a space; anything unparsable is null.</summary>
    internal static double? ParsePercent(string? text)
    {
        var digits = text?.Replace("%", "").Trim();
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 100
            ? value
            : null;
    }
}
