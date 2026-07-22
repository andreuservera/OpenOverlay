using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Live incident count — the number that actually determines whether Safety Rating moves up or down
/// after the session, surfaced during the session instead of only after it.
/// </summary>
internal static class IncidentBuilder
{
    public static IncidentState Build(TelemetrySnapshot telemetry)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.PlayerCarMyIncidentCount))
        {
            return IncidentState.Empty;
        }

        var mine = telemetry.GetInt(TelemetryVarNames.PlayerCarMyIncidentCount);
        int? team = telemetry.HasVariable(TelemetryVarNames.PlayerCarTeamIncidentCount)
            ? telemetry.GetInt(TelemetryVarNames.PlayerCarTeamIncidentCount)
            : null;

        return new IncidentState { MyIncidentCount = mine, TeamIncidentCount = team };
    }
}
