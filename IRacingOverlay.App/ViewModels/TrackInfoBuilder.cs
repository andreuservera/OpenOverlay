using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Combines the static per-weekend track name (YAML, WeekendInfo) with live weather/session-clock
/// telemetry (updates every tick, unlike WeekendInfo which only reflects conditions at session
/// start) into the horizontal "track info" bar's data.
/// </summary>
internal static class TrackInfoBuilder
{
    // Same sentinel-handling approach as FuelBuilder: iRacing reports an implausibly large number
    // rather than a null/-1 when a session has no lap or time limit.
    private const int NoLapLimitThreshold = 20_000;
    private const double NoTimeLimitThresholdSeconds = 1_000_000;

    public static TrackInfoState Build(TelemetrySnapshot telemetry, IracingSessionInfo? session)
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
        if (session?.SessionInfo is { } sessionInfo)
        {
            var current = sessionInfo.Sessions.FirstOrDefault(s => s.SessionNum == sessionInfo.CurrentSessionNum);
            sessionLabel = current?.SessionName;
            if (string.IsNullOrWhiteSpace(sessionLabel))
            {
                sessionLabel = current?.SessionType ?? "";
            }
        }

        double? timeRemaining = null;
        if (telemetry.HasVariable(TelemetryVarNames.SessionTimeRemain))
        {
            var raw = telemetry.GetDouble(TelemetryVarNames.SessionTimeRemain);
            if (raw >= 0 && raw < NoTimeLimitThresholdSeconds)
            {
                timeRemaining = raw;
            }
        }

        int? lapsRemaining = null;
        if (telemetry.HasVariable(TelemetryVarNames.SessionLapsRemain))
        {
            var raw = telemetry.GetInt(TelemetryVarNames.SessionLapsRemain);
            if (raw is > 0 and < NoLapLimitThreshold)
            {
                lapsRemaining = raw;
            }
        }

        return new TrackInfoState
        {
            TrackName = trackName ?? "",
            SessionLabel = sessionLabel ?? "",
            AirTempC = GetFloatOrZero(telemetry, TelemetryVarNames.AirTemp),
            TrackTempC = GetFloatOrZero(telemetry, TelemetryVarNames.TrackTempCrew),
            WindSpeedMs = GetFloatOrZero(telemetry, TelemetryVarNames.WindVel),
            HumidityPct = GetFloatOrZero(telemetry, TelemetryVarNames.RelativeHumidity),
            TimeRemainingSeconds = timeRemaining,
            LapsRemaining = lapsRemaining,
        };
    }

    private static double GetFloatOrZero(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetFloat(name) : 0;
}
