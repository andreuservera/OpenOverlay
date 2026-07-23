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
    public const string Throttle = "Throttle";
    public const string Brake = "Brake";
    public const string Clutch = "Clutch";
    public const string Lap = "Lap";
    public const string LapDistPct = "LapDistPct";
    public const string SessionTime = "SessionTime";
    public const string SessionState = "SessionState";
    /// <summary>bool — confirmed via iRacing's own SDK docs: "true only when the player is running
    /// the physics for the car and is currently in the car," i.e. false at the main menu, on a
    /// garage/setup screen, spectating, or watching a replay — even if a car is sitting out on
    /// track. This is "is the driver actually driving," not "is the car on pit road" (CarIdxOnPitRoad
    /// covers that, per-car, elsewhere below).</summary>
    public const string IsOnTrack = "IsOnTrack";
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
    /// <summary>Enum irsdk_CarLeftRight, confirmed live to be typed as a plain Int (not a bitfield,
    /// despite what the docs say): 0=off,1=clear,2=car left,3=car right,4=car both sides,
    /// 5=two cars left,6=two cars right.</summary>
    public const string CarLeftRight = "CarLeftRight";

    /// <summary>uint bitfield, irsdk_Flags — see FlagBuilder for the bit layout.</summary>
    public const string SessionFlags = "SessionFlags";

    /// <summary>
    /// Tire variable name for one corner ("LF"/"RF"/"LR"/"RR"). Some cars only update the live
    /// "hot" pressure/temp while sitting in the pit stall (no in-car TPMS) — confirmed real iRacing
    /// behavior, not a bug here: read it every tick regardless and it'll just look "frozen" between
    /// pit visits for those cars.
    /// </summary>
    public static string TireColdPressure(string corner) => $"{corner}coldPressure";
    public static string TirePressure(string corner) => $"{corner}pressure";

    /// <summary>
    /// "CL/CM/CR" (Carcass Left/Middle/Right) — the tire's internal structural temperature, which is
    /// what the garage/setup screen shows. This is the only one of the two still in iRacing's current
    /// telemetry spec.
    /// </summary>
    public static string TireTempCarcassLeft(string corner) => $"{corner}tempCL";
    public static string TireTempCarcassMiddle(string corner) => $"{corner}tempCM";
    public static string TireTempCarcassRight(string corner) => $"{corner}tempCR";

    /// <summary>
    /// "L/M/R" surface temperature (no "C") — closer to what an in-car dash actually displays
    /// (iRacing's own developer blog describes it as an instantaneous, infrared-sensor-like reading,
    /// versus the garage's carcass/pyrometer-style reading). Documented as a legacy 2015-era name and
    /// may not exist live on every car/build, hence tried first with a fallback to carcass temp.
    /// </summary>
    public static string TireTempSurfaceLeft(string corner) => $"{corner}tempL";
    public static string TireTempSurfaceMiddle(string corner) => $"{corner}tempM";
    public static string TireTempSurfaceRight(string corner) => $"{corner}tempR";

    /// <summary>Remaining tread fraction (1.0 = new, 0.0 = fully worn), three tread zones per corner —
    /// a separate telemetry channel from pressure/temp.</summary>
    public static string TireWearLeft(string corner) => $"{corner}wearL";
    public static string TireWearMiddle(string corner) => $"{corner}wearM";
    public static string TireWearRight(string corner) => $"{corner}wearR";

    /// <summary>
    /// iRacing computes these deltas itself — no need to derive them from lap-distance interpolation.
    /// Each has a companion "_OK" bool (valid this tick) confirmed via community documentation.
    /// </summary>
    public const string DeltaToSessionBestLap = "LapDeltaToSessionBestLap";
    public const string DeltaToSessionBestLapOk = "LapDeltaToSessionBestLap_OK";
    /// <summary>Rate of change of the delta itself (seconds of gap per second of real time) — negative
    /// means currently gaining on the reference lap, positive means currently losing ground, near-zero
    /// means holding steady. Used to drive delta-display color intensity independent of the raw
    /// delta's own sign.</summary>
    public const string DeltaToSessionBestLapRate = "LapDeltaToSessionBestLap_DD";
    /// <summary>Despite the plain name, this is the driver's personal best across *all* past
    /// sessions (career-wide), not just the current one — confirmed via community documentation.</summary>
    public const string DeltaToBestLap = "LapDeltaToBestLap";
    public const string DeltaToBestLapOk = "LapDeltaToBestLap_OK";
    public const string DeltaToBestLapRate = "LapDeltaToBestLap_DD";
    /// <summary>Delta to a theoretical lap built from the driver's own best individual sector times.</summary>
    public const string DeltaToOptimalLap = "LapDeltaToOptimalLap";
    public const string DeltaToOptimalLapOk = "LapDeltaToOptimalLap_OK";
    public const string DeltaToOptimalLapRate = "LapDeltaToOptimalLap_DD";

    public const string FuelLevel = "FuelLevel";
    public const string FuelLevelPct = "FuelLevelPct";
    /// <summary>iRacing's own live consumption-rate estimate, in liters/hour — no need to derive it
    /// from a fuel-level delta ourselves.</summary>
    public const string FuelUsePerHour = "FuelUsePerHour";
    /// <summary>Large sentinel value (iRacing uses a very large int, not a negative one) when the
    /// session has no lap limit (e.g. a timed or open practice session) — treat anything absurdly
    /// large as "no limit" rather than a real number of laps.</summary>
    public const string SessionLapsRemain = "SessionLapsRemainEx";
    public const string SessionTimeRemain = "SessionTimeRemain";

    public const string PlayerCarMyIncidentCount = "PlayerCarMyIncidentCount";
    public const string PlayerCarTeamIncidentCount = "PlayerCarTeamIncidentCount";

    /// <summary>All measured "at the start/finish line" per iRacing's own variable descriptions —
    /// live weather, unlike WeekendInfo's YAML fields which only reflect conditions at session start.</summary>
    public const string AirTemp = "AirTemp";
    /// <summary>Not "TrackTemp" (that one's documented as deprecated, kept only for back-compat).</summary>
    public const string TrackTempCrew = "TrackTempCrew";
    public const string WindVel = "WindVel";
    public const string RelativeHumidity = "RelativeHumidity";
}
