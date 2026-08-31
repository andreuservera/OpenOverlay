using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Which of the weekend's sessions is running right now.
///
/// This has to come from the <c>SessionNum</c> telemetry variable, not from the session YAML.
/// iRacing's own SDK documentation is explicit that "SessionInfo contains a single child parameter
/// of Sessions" — there is no current-session key in that blob at all, so the
/// <see cref="SessionInfoSection.CurrentSessionNum"/> field never gets deserialised and sits at its
/// default of 0 forever. Anything that looked up "the current session" through it was really
/// looking up session 0, which on a normal race weekend is Practice: the standings widget then ran
/// its qualifying-style fastest-lap ranking during the actual race, listing the whole lobby in
/// roster order with no BEST because CarIdxBestLapTime resets per session and nobody had set a
/// race lap yet.
/// </summary>
internal static class CurrentSession
{
    public static int Number(TelemetrySnapshot telemetry, IracingSessionInfo? session) =>
        telemetry.HasVariable(TelemetryVarNames.SessionNum)
            ? telemetry.GetInt(TelemetryVarNames.SessionNum)
            : session?.SessionInfo?.CurrentSessionNum ?? 0;

    /// <summary>The running session's entry, or null when it can't be identified — deliberately no
    /// "just take the first one" fallback, since guessing session 0 is the exact bug above.</summary>
    public static SessionEntry? Entry(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (session?.SessionInfo is not { } sessionInfo)
        {
            return null;
        }

        var number = Number(telemetry, session);
        return sessionInfo.Sessions.FirstOrDefault(s => s.SessionNum == number);
    }

    /// <summary>The running session's scoring table, keyed by CarIdx. Empty when the session has no
    /// results yet (nobody has crossed the line) or can't be identified.</summary>
    public static Dictionary<int, SessionResultPosition> Results(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        var results = new Dictionary<int, SessionResultPosition>();
        foreach (var position in Entry(telemetry, session)?.ResultsPositions ?? [])
        {
            if (position.CarIdx >= 0)
            {
                results[position.CarIdx] = position;
            }
        }

        return results;
    }
}
