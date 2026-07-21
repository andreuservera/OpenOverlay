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

        // Prefer surface temp (closer to what an in-car dash shows — see TelemetryVarNames) and only
        // fall back to carcass temp if the surface variables aren't present on this build/car.
        var hasSurfaceTemp = telemetry.HasVariable(TelemetryVarNames.TireTempSurfaceLeft(corner));
        var left = hasSurfaceTemp
            ? TryGetFloat(telemetry, TelemetryVarNames.TireTempSurfaceLeft(corner))
            : TryGetFloat(telemetry, TelemetryVarNames.TireTempCarcassLeft(corner));
        var middle = hasSurfaceTemp
            ? TryGetFloat(telemetry, TelemetryVarNames.TireTempSurfaceMiddle(corner))
            : TryGetFloat(telemetry, TelemetryVarNames.TireTempCarcassMiddle(corner));
        var right = hasSurfaceTemp
            ? TryGetFloat(telemetry, TelemetryVarNames.TireTempSurfaceRight(corner))
            : TryGetFloat(telemetry, TelemetryVarNames.TireTempCarcassRight(corner));

        return new TireCornerInfo
        {
            Label = corner,
            PressureKPa = pressure ?? 0,
            ColdPressureKPa = coldPressure ?? 0,
            TempLeft = left ?? 0,
            TempMiddle = middle ?? 0,
            TempRight = right ?? 0,
            IsSurfaceTemp = hasSurfaceTemp,
        };
    }

    private static float? TryGetFloat(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetFloat(name) : null;
}
