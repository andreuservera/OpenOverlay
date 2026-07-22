using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Reads iRacing's own precomputed lap delta variables — no need to derive them ourselves from
/// lap-distance interpolation, iRacing already does that internally.
/// </summary>
internal static class DeltaBuilder
{
    public static DeltaState Build(TelemetrySnapshot telemetry, DeltaReference reference)
    {
        var (varName, okName, ddName, label) = reference switch
        {
            DeltaReference.SessionBest => (TelemetryVarNames.DeltaToSessionBestLap, TelemetryVarNames.DeltaToSessionBestLapOk, TelemetryVarNames.DeltaToSessionBestLapRate, "VS SESSION BEST"),
            DeltaReference.PersonalBestAllTime => (TelemetryVarNames.DeltaToBestLap, TelemetryVarNames.DeltaToBestLapOk, TelemetryVarNames.DeltaToBestLapRate, "VS ALL-TIME BEST"),
            DeltaReference.OptimalLap => (TelemetryVarNames.DeltaToOptimalLap, TelemetryVarNames.DeltaToOptimalLapOk, TelemetryVarNames.DeltaToOptimalLapRate, "VS OPTIMAL LAP"),
            _ => (TelemetryVarNames.DeltaToSessionBestLap, TelemetryVarNames.DeltaToSessionBestLapOk, TelemetryVarNames.DeltaToSessionBestLapRate, "VS SESSION BEST"),
        };

        if (!telemetry.HasVariable(varName))
        {
            return new DeltaState { DeltaSeconds = 0, RateOfChange = 0, IsValid = false, ReferenceLabel = label };
        }

        // The "_OK" companion (when present) says whether this tick's delta is actually meaningful
        // (e.g. not valid before a reference lap exists yet) — 0 is a legitimate "dead even" delta,
        // so it can't be used by itself to mean "no data." Its exact declared type isn't confirmed
        // from a primary source, so read defensively (same lesson as CarLeftRight/SessionFlags).
        var isValid = !telemetry.HasVariable(okName) || ReadBoolLike(telemetry, okName);
        var rateOfChange = telemetry.HasVariable(ddName) ? telemetry.GetFloat(ddName) : 0;

        return new DeltaState
        {
            DeltaSeconds = telemetry.GetFloat(varName),
            RateOfChange = rateOfChange,
            IsValid = isValid,
            ReferenceLabel = label,
        };
    }

    private static bool ReadBoolLike(TelemetrySnapshot telemetry, string name)
    {
        try
        {
            return telemetry.GetBool(name);
        }
        catch (InvalidOperationException)
        {
            return telemetry.GetInt(name) != 0;
        }
    }
}
