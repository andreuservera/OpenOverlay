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

    private static TireCornerInfo BuildCorner(TelemetrySnapshot telemetry, string corner)
    {
        var pressure = TryGetFloat(telemetry, TelemetryVarNames.TirePressure(corner));
        var coldPressure = TryGetFloat(telemetry, TelemetryVarNames.TireColdPressure(corner));

        // Prefer surface temp (closer to what an in-car dash shows — see TelemetryVarNames), but
        // "prefer" has to mean "actually has a live value," not just "the variable name is declared."
        // The earlier version gated on HasVariable alone — this is a real bug if the legacy surface-
        // temp name is still present in the var-header table on a given build/car but never actually
        // written to (stays 0 forever), which would lock onto three permanent zeros and starve the
        // panel of the live carcass values it should have fallen back to. Gate on an actual nonzero
        // reading instead.
        var surfaceLeft = TryGetFloat(telemetry, TelemetryVarNames.TireTempSurfaceLeft(corner));
        var surfaceMiddle = TryGetFloat(telemetry, TelemetryVarNames.TireTempSurfaceMiddle(corner));
        var surfaceRight = TryGetFloat(telemetry, TelemetryVarNames.TireTempSurfaceRight(corner));
        var hasUsableSurfaceTemp = (surfaceLeft ?? 0) > 0 || (surfaceMiddle ?? 0) > 0 || (surfaceRight ?? 0) > 0;

        var left = hasUsableSurfaceTemp ? surfaceLeft : TryGetFloat(telemetry, TelemetryVarNames.TireTempCarcassLeft(corner));
        var middle = hasUsableSurfaceTemp ? surfaceMiddle : TryGetFloat(telemetry, TelemetryVarNames.TireTempCarcassMiddle(corner));
        var right = hasUsableSurfaceTemp ? surfaceRight : TryGetFloat(telemetry, TelemetryVarNames.TireTempCarcassRight(corner));

        var wearLeft = TryGetFloat(telemetry, TelemetryVarNames.TireWearLeft(corner));
        var wearMiddle = TryGetFloat(telemetry, TelemetryVarNames.TireWearMiddle(corner));
        var wearRight = TryGetFloat(telemetry, TelemetryVarNames.TireWearRight(corner));
        var hasWearData = wearLeft.HasValue || wearMiddle.HasValue || wearRight.HasValue;

        return new TireCornerInfo
        {
            Label = corner,
            PressureKPa = pressure ?? 0,
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
}
