using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Reads live tire pressure/temperature from telemetry. Confirmed real iRacing behavior, not a bug
/// here: cars without an in-car TPMS only get the "hot" pressure/temp refreshed while sitting in the
/// pit stall, so these values will simply look frozen between pit visits on those cars — this reads
/// whatever's there every tick regardless, which is the correct behavior either way.
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
        var tempLeft = TryGetFloat(telemetry, TelemetryVarNames.TireTempLeft(corner));
        var tempMiddle = TryGetFloat(telemetry, TelemetryVarNames.TireTempMiddle(corner));
        var tempRight = TryGetFloat(telemetry, TelemetryVarNames.TireTempRight(corner));

        var temps = new[] { tempLeft, tempMiddle, tempRight }.Where(t => t is > 0).Select(t => t!.Value).ToArray();
        var avgTemp = temps.Length > 0 ? temps.Average() : 0;

        return new TireCornerInfo
        {
            Label = corner,
            PressureKPa = pressure ?? 0,
            ColdPressureKPa = coldPressure ?? 0,
            TempC = avgTemp,
        };
    }

    private static float? TryGetFloat(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetFloat(name) : null;
}
