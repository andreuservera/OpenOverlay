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
    /// <summary>Radians at the wheel rim, positive turning left (anticlockwise).</summary>
    public const string SteeringWheelAngle = "SteeringWheelAngle";
    public const string Throttle = "Throttle";
    public const string Brake = "Brake";
    /// <summary>The player's clutch pedal alone. "Clutch" is what the physics applied, including
    /// iRacing's auto-clutch, anti-stall and launch assists.</summary>
    public const string ClutchRaw = "ClutchRaw";
    public const string Lap = "Lap";
    public const string LapDistPct = "LapDistPct";
    public const string SessionTime = "SessionTime";
    public const string SessionState = "SessionState";
    /// <summary>int — which of the weekend's sessions is running. The only reliable source: the
    /// session YAML has no equivalent key (see <see cref="CurrentSession"/>).</summary>
    public const string SessionNum = "SessionNum";
    /// <summary>bool — confirmed via iRacing's own SDK docs: "true only when the player is running
    /// the physics for the car and is currently in the car," i.e. false at the main menu, on a
    /// garage/setup screen, spectating, or watching a replay — even if a car is sitting out on
    /// track. This is "is the driver actually driving," not "is the car on pit road" (CarIdxOnPitRoad
    /// covers that, per-car, elsewhere below).</summary>
    public const string IsOnTrack = "IsOnTrack";
    /// <summary>bool — the player's car is on pit road, between the cones.</summary>
    public const string OnPitRoad = "OnPitRoad";
    /// <summary>int — laps completed by the player (Lap is laps started).</summary>
    public const string LapCompleted = "LapCompleted";
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
    /// <summary>int[] — laps each car has completed; steps up as it crosses the line.</summary>
    public const string CarIdxLapCompleted = "CarIdxLapCompleted";
    /// <summary>float[], seconds — iRacing's estimate of how long each car takes to get from the line to
    /// where it is now. It follows the car's speed round the lap, not just the distance — a car on a
    /// straight covers far more track per second than one in a hairpin — which is what makes a gap
    /// built from it read like iRacing's own relative. Each car runs on its own CarClassEstLapTime
    /// clock, so two cars' values only compare directly when they are the same car.</summary>
    public const string CarIdxEstTime = "CarIdxEstTime";
    /// <summary>int[] — each car's current tyre, an index into DriverInfo.DriverTires; -1 when unknown.</summary>
    public const string CarIdxTireCompound = "CarIdxTireCompound";

    public const string BrakeAbsActive = "BrakeABSactive";
    /// <summary>float — the in-car ABS setting. Only present on cars with adjustable ABS.</summary>
    public const string AbsSetting = "dcABS";
    /// <summary>float — the in-car traction-control setting, whole numbers (GR86: 1, 992 GT3 R: 3 in
    /// recordings). Only present on cars with adjustable TC.</summary>
    public const string TractionControl = "dcTractionControl";
    /// <summary>Enum irsdk_CarLeftRight, confirmed live to be typed as a plain Int (not a bitfield,
    /// despite what the docs say): 0=off,1=clear,2=car left,3=car right,4=car both sides,
    /// 5=two cars left,6=two cars right.</summary>
    public const string CarLeftRight = "CarLeftRight";

    /// <summary>uint bitfield, irsdk_Flags — see FlagBuilder for the bit layout.</summary>
    public const string SessionFlags = "SessionFlags";
    /// <summary>uint bitfield[] per CarIdx, same irsdk_Flags layout as <see cref="SessionFlags"/>.</summary>
    public const string CarIdxSessionFlags = "CarIdxSessionFlags";

    /// <summary>
    /// Tire variable name for one corner ("LF"/"RF"/"LR"/"RR"). Confirmed via iRacing's own published
    /// telemetry variable list: there is no live/"hot" tire pressure channel at all — cold/garage-set
    /// pressure (as last set in the garage, refreshed on pit stops) is the only pressure telemetry
    /// iRacing actually exposes for any car. An earlier version of this code assumed a "{corner}
    /// pressure" hot-pressure variable also existed and read it first — it never does, on any car, so
    /// the pressure display always fell back to "—" instead of showing the cold pressure it could have.
    /// </summary>
    public static string TireColdPressure(string corner) => $"{corner}coldPressure";

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
    /// <summary>Engine coolant and oil temperature, degrees C.</summary>
    public const string WaterTemp = "WaterTemp";
    public const string OilTemp = "OilTemp";
    /// <summary>iRacing's own live consumption-rate estimate, in liters/hour — no need to derive it
    /// from a fuel-level delta ourselves.</summary>
    public const string FuelUsePerHour = "FuelUsePerHour";
    /// <summary>Large sentinel value (iRacing uses a very large int, not a negative one) when the
    /// session has no lap limit (e.g. a timed or open practice session) — treat anything absurdly
    /// large as "no limit" rather than a real number of laps.</summary>
    public const string SessionLapsRemain = "SessionLapsRemainEx";
    public const string SessionTimeRemain = "SessionTimeRemain";
    /// <summary>int — the session's lap count; the same no-limit sentinel as the laps remaining.</summary>
    public const string SessionLapsTotal = "SessionLapsTotal";
    /// <summary>double, seconds — the session's length; the same 7-day sentinel when it has none.</summary>
    public const string SessionTimeTotal = "SessionTimeTotal";

    public const string PlayerCarMyIncidentCount = "PlayerCarMyIncidentCount";
    public const string PlayerCarTeamIncidentCount = "PlayerCarTeamIncidentCount";
    /// <summary>bitfield, irsdk_IncidentFlags — "log incidents that the player received": what
    /// happened in the low byte, what it cost in the next. See <see cref="IncidentReport"/>.</summary>
    public const string PlayerIncidents = "PlayerIncidents";

    /// <summary>All measured "at the start/finish line" per iRacing's own variable descriptions —
    /// live weather, unlike WeekendInfo's YAML fields which only reflect conditions at session start.</summary>
    public const string AirTemp = "AirTemp";

    /// <summary>The in-car brake bias setting, as the car's black box shows it (front %, on most cars).</summary>
    public const string BrakeBias = "dcBrakeBias";
    /// <summary>Not "TrackTemp" (that one's documented as deprecated, kept only for back-compat).</summary>
    public const string TrackTempCrew = "TrackTempCrew";
    public const string WindVel = "WindVel";
    /// <summary>Bearing in radians, clockwise from north.</summary>
    public const string WindDir = "WindDir";
    /// <summary>Reported as a 0-1 fraction despite iRacing documenting the unit as "%" — multiply by
    /// 100 before display.</summary>
    public const string RelativeHumidity = "RelativeHumidity";
    /// <summary>Car heading, radians clockwise from north — the reference for relative wind.</summary>
    public const string YawNorth = "YawNorth";
    /// <summary>int — 0 clear, 1 partly cloudy, 2 mostly cloudy, 3 overcast.</summary>
    public const string Skies = "Skies";
    /// <summary>Precipitation at the start/finish line, a 0-1 fraction like humidity.</summary>
    public const string Precipitation = "Precipitation";
    /// <summary>Sun angle above the horizon in radians; below zero is night.</summary>
    public const string SolarAltitude = "SolarAltitude";
    public const string WeatherDeclaredWet = "WeatherDeclaredWet";
    /// <summary>int — irsdk_TrackWetness: 0 unknown, 1 dry … 7 extremely wet.</summary>
    public const string TrackWetness = "TrackWetness";
    /// <summary>int — the sim's unit setting: 0 = English (imperial), 1 = metric.</summary>
    public const string DisplayUnits = "DisplayUnits";
}
