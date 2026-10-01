using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Live incident count — the number that actually determines whether Safety Rating moves up or down
/// after the session, surfaced during the session instead of only after it.
/// </summary>
internal static class IncidentBuilder
{
    public static IncidentState Build(TelemetrySnapshot telemetry, IracingSessionInfo? session = null, IncidentReport? latestReport = null)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.PlayerCarMyIncidentCount))
        {
            return IncidentState.Empty;
        }

        var mine = telemetry.GetInt(TelemetryVarNames.PlayerCarMyIncidentCount);
        // The team variable exists in every session; it only means something in a team race.
        int? team = session?.WeekendInfo?.TeamRacing == 1 && telemetry.HasVariable(TelemetryVarNames.PlayerCarTeamIncidentCount)
            ? telemetry.GetInt(TelemetryVarNames.PlayerCarTeamIncidentCount)
            : null;
        int? limit = int.TryParse(session?.WeekendInfo?.WeekendOptions?.IncidentLimit, out var parsed) && parsed > 0
            ? parsed
            : null;

        return new IncidentState { MyIncidentCount = mine, TeamIncidentCount = team, Limit = limit, LatestReport = latestReport };
    }
}
