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
    /// <summary>float[], seconds — "estimated time to reach current location on track" per car.
    /// The precise, class-agnostic building block for relative gaps (confirmed via iRacing SDK docs).</summary>
    public const string CarIdxEstTime = "CarIdxEstTime";

    public const string BrakeAbsActive = "BrakeABSactive";
    /// <summary>Best available proxy for TC engagement; iRacing has no documented real-time
    /// "TC is cutting power right now" flag like BrakeABSactive. This "dc" (driver control) variable
    /// most likely reflects whether TC is toggled on, not live intervention — needs live confirmation.</summary>
    public const string TractionControlToggle = "dcTractionControlToggle";
    /// <summary>Enum irsdk_CarLeftRight, confirmed live to be typed as a plain Int (not a bitfield,
    /// despite what the docs say): 0=off,1=clear,2=car left,3=car right,4=car both sides,
    /// 5=two cars left,6=two cars right.</summary>
    public const string CarLeftRight = "CarLeftRight";
}
