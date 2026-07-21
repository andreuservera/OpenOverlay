namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Names of the iRacing telemetry variables the v1 overlays consume. Centralized so a rename or
/// a variable that turns out to be missing on some cars only needs a defensive check in one place.
/// </summary>
internal static class TelemetryVarNames
{
    public const string Speed = "Speed";
    public const string Rpm = "RPM";
    public const string Gear = "Gear";
    public const string Lap = "Lap";
    public const string LapDistPct = "LapDistPct";
    public const string SessionTime = "SessionTime";
    public const string SessionState = "SessionState";
    public const string PlayerLastLapTime = "LapLastLapTime";
    public const string PlayerBestLapTime = "LapBestLapTime";

    public const string CarIdxLap = "CarIdxLap";
    public const string CarIdxLapDistPct = "CarIdxLapDistPct";
    public const string CarIdxPosition = "CarIdxPosition";
    public const string CarIdxClassPosition = "CarIdxClassPosition";
    public const string CarIdxOnPitRoad = "CarIdxOnPitRoad";
    public const string CarIdxTrackSurface = "CarIdxTrackSurface";
    public const string CarIdxF2Time = "CarIdxF2Time";
    public const string CarIdxBestLapTime = "CarIdxBestLapTime";
    public const string CarIdxLastLapTime = "CarIdxLastLapTime";
}
