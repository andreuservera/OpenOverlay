using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Reads live tire pressure/temperature from telemetry. Confirmed real iRacing behavior, not a bug
/// here: cars without an in-car TPMS only get the "hot" pressure/temp refreshed while sitting in the
/// pit stall, so these values will simply look frozen between pit visits on those cars — this reads
/// whatever's there every tick regardless, which is the correct behavior either way.
/// Shows all three tread zones (inner/middle/outer) per corner rather than one averaged number,
/// matching how a real dash/telemetry display actually presents it.
/// </summary>
internal static class TireInfoBuilder
{
    public static TireInfoState Build(TelemetrySnapshot telemetry) => new()
    {
        LF = BuildCorner(telemetry, "LF"),
        RF = BuildCorner(telemetry, "RF"),
        LR = BuildCorner(telemetry, "LR"),
        RR = BuildCorner(telemetry, "RR"),
    };

    // Sanity bounds for readings that come from a variable name iRacing's var-header table can carry
    // even when a given car/build never actually writes a live value into it (see the surface-temp
    // fallback below) — reported live as the tire panel showing "random numbers": whatever stale or
    // uninitialized bytes are sitting in that slot get reinterpreted as a float and, unlike a clean
    // 0, can come out as an enormous or nonsensical value that still passes a bare "> 0" check.
    // These are generous (real tire temps/pressures never get remotely close) purely to reject that
    // garbage while never rejecting a genuine reading.
    private const float MaxPlausibleTempC = 300f;
    private const float MaxPlausiblePressureKPa = 500f;

    private static TireCornerInfo BuildCorner(TelemetrySnapshot telemetry, string corner)
    {
        // Confirmed via iRacing's own telemetry variable list: there is no live/"hot" pressure
        // channel at all, on any car — cold/garage-set pressure is the only one that exists.
        var coldPressure = TryGetPlausibleFloat(telemetry, TelemetryVarNames.TireColdPressure(corner), MaxPlausiblePressureKPa);

        // Prefer surface temp (closer to what an in-car dash shows — see TelemetryVarNames), but
        // "prefer" has to mean "actually has a live, plausible value," not just "the variable name is
        // declared." Gating on HasVariable alone breaks if the legacy surface-temp name is still
        // present in the var-header table on a given build/car but never actually written to (stays
        // 0 forever, or worse, holds leftover garbage) — either way it should be ignored in favor of
        // the live carcass values.
        var surfaceLeft = TryGetPlausibleFloat(telemetry, TelemetryVarNames.TireTempSurfaceLeft(corner), MaxPlausibleTempC);
        var surfaceMiddle = TryGetPlausibleFloat(telemetry, TelemetryVarNames.TireTempSurfaceMiddle(corner), MaxPlausibleTempC);
        var surfaceRight = TryGetPlausibleFloat(telemetry, TelemetryVarNames.TireTempSurfaceRight(corner), MaxPlausibleTempC);
        var hasUsableSurfaceTemp = (surfaceLeft ?? 0) > 0 || (surfaceMiddle ?? 0) > 0 || (surfaceRight ?? 0) > 0;

        var left = hasUsableSurfaceTemp ? surfaceLeft : TryGetPlausibleFloat(telemetry, TelemetryVarNames.TireTempCarcassLeft(corner), MaxPlausibleTempC);
        var middle = hasUsableSurfaceTemp ? surfaceMiddle : TryGetPlausibleFloat(telemetry, TelemetryVarNames.TireTempCarcassMiddle(corner), MaxPlausibleTempC);
        var right = hasUsableSurfaceTemp ? surfaceRight : TryGetPlausibleFloat(telemetry, TelemetryVarNames.TireTempCarcassRight(corner), MaxPlausibleTempC);

        var wearLeft = TryGetFloat(telemetry, TelemetryVarNames.TireWearLeft(corner));
        var wearMiddle = TryGetFloat(telemetry, TelemetryVarNames.TireWearMiddle(corner));
        var wearRight = TryGetFloat(telemetry, TelemetryVarNames.TireWearRight(corner));
        var hasWearData = wearLeft.HasValue || wearMiddle.HasValue || wearRight.HasValue;

        return new TireCornerInfo
        {
            Label = corner,
            PressureKPa = coldPressure ?? 0,
            ColdPressureKPa = coldPressure ?? 0,
            TempLeft = left ?? 0,
            TempMiddle = middle ?? 0,
            TempRight = right ?? 0,
            IsSurfaceTemp = hasUsableSurfaceTemp,
            WearLeft = wearLeft ?? 1.0,
            WearMiddle = wearMiddle ?? 1.0,
            WearRight = wearRight ?? 1.0,
            HasWearData = hasWearData,
        };
    }

    private static float? TryGetFloat(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetFloat(name) : null;

    private static float? TryGetPlausibleFloat(TelemetrySnapshot telemetry, string name, float max)
    {
        if (!telemetry.HasVariable(name))
        {
            return null;
        }

        var value = telemetry.GetFloat(name);
        return float.IsFinite(value) && value > 0 && value <= max ? value : null;
    }
}
