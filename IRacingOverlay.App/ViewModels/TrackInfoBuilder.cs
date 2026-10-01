using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Combines the static per-weekend track name (YAML, WeekendInfo) with live weather/session-clock
/// telemetry (updates every tick, unlike WeekendInfo which only reflects conditions at session
/// start) into the horizontal "track info" bar's data.
/// </summary>
internal static class TrackInfoBuilder
{
    /// <param name="recentRacingLapSeconds">The player's recent racing pace (<see cref="LapLog"/>), for
    /// the lap total of a timed session; 0 falls back to their last/best lap.</param>
    public static TrackInfoState Build(TelemetrySnapshot telemetry, IracingSessionInfo? session, double recentRacingLapSeconds = 0)
    {
        var trackName = session?.WeekendInfo?.TrackDisplayShortName;
        if (string.IsNullOrWhiteSpace(trackName))
        {
            trackName = session?.WeekendInfo?.TrackDisplayName;
        }

        if (string.IsNullOrWhiteSpace(trackName))
        {
            trackName = session?.WeekendInfo?.TrackName ?? "";
        }

        var sessionLabel = "";
        var trackUsage = "";
        if (CurrentSession.Entry(telemetry, session) is { } current)
        {
            sessionLabel = current.SessionName;
            if (string.IsNullOrWhiteSpace(sessionLabel))
            {
                sessionLabel = current.SessionType ?? "";
            }

            trackUsage = current.SessionTrackRubberState ?? "";
        }

        return new TrackInfoState
        {
            TrackName = trackName ?? "",
            SessionLabel = sessionLabel ?? "",
            TrackUsage = trackUsage ?? "",
            AirTempC = GetFloatOrZero(telemetry, TelemetryVarNames.AirTemp),
            TrackTempC = GetFloatOrZero(telemetry, TelemetryVarNames.TrackTempCrew),
            WindSpeedMs = GetFloatOrZero(telemetry, TelemetryVarNames.WindVel),
            WindDirRad = GetFloatOrZero(telemetry, TelemetryVarNames.WindDir),
            // iRacing's "%" unit is a 0-1 fraction (same as Throttle/FuelLevelPct), so 38% arrives as 0.38.
            HumidityPct = GetFloatOrZero(telemetry, TelemetryVarNames.RelativeHumidity) * 100,
            TimeRemainingSeconds = SessionClock.TimeRemaining(telemetry),
            Progress = SessionProgressBuilder.Build(telemetry, session, recentRacingLapSeconds),
            UnitSystem = Units.Read(telemetry),
        };
    }

    private static double GetFloatOrZero(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetFloat(name) : 0;
}
