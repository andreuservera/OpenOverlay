namespace IRacingOverlay.Coaching.Telemetry;

/// <summary>
/// The player-car channels the coach needs from one telemetry tick, in SI units as iRacing reports
/// them (m, m/s, m/s², rad, rad/s; pedals 0–1). One shape for both sources — the live shared memory
/// (<see cref="SnapshotFrames"/>) and .ibt files (<see cref="Ibt.IbtFile"/>) — so everything downstream
/// is tested once and runs the same on either.
/// </summary>
public readonly record struct TelemetryFrame
{
    public double SessionTime { get; init; }
    public int SessionNum { get; init; }

    /// <summary>irsdk_SessionState: 1 get in car, 2 warmup, 3 parade, 4 racing, 5 checkered, 6 cool down.</summary>
    public int SessionState { get; init; }

    public int Lap { get; init; }

    /// <summary>Metres from the start/finish line along the lap.</summary>
    public float LapDist { get; init; }

    /// <summary>Fraction of the lap, 0–1.</summary>
    public float LapDistPct { get; init; }

    /// <summary>iRacing's own time for the last completed lap, seconds; ≤ 0 when none.</summary>
    public float LapLastLapTime { get; init; }

    public float Speed { get; init; }
    public float Throttle { get; init; }
    public float Brake { get; init; }
    public float Clutch { get; init; }
    public float SteeringWheelAngle { get; init; }
    public int Gear { get; init; }
    public float Rpm { get; init; }

    public float LatAccel { get; init; }
    public float LongAccel { get; init; }
    public float YawRate { get; init; }

    public bool AbsActive { get; init; }
    public bool OnPitRoad { get; init; }
    public bool IsOnTrack { get; init; }

    /// <summary>irsdk_TrkLoc under the player's car; see <see cref="TrackSurface"/>.</summary>
    public int TrackSurface { get; init; }

    public int IncidentCount { get; init; }

    /// <summary>GPS position in degrees. Only .ibt files carry it; null live.</summary>
    public double? Latitude { get; init; }

    public double? Longitude { get; init; }
}

/// <summary>irsdk_TrkLoc values.</summary>
public static class TrackSurface
{
    public const int NotInWorld = -1;
    public const int OffTrack = 0;
    public const int InPitStall = 1;
    public const int ApproachingPits = 2;
    public const int OnTrack = 3;
}

/// <summary>The iRacing variable names read into a <see cref="TelemetryFrame"/>.</summary>
internal static class FrameVars
{
    public const string SessionTime = "SessionTime";
    public const string SessionNum = "SessionNum";
    public const string SessionState = "SessionState";
    public const string Lap = "Lap";
    public const string LapDist = "LapDist";
    public const string LapDistPct = "LapDistPct";
    public const string LapLastLapTime = "LapLastLapTime";
    public const string Speed = "Speed";
    public const string Throttle = "Throttle";
    public const string Brake = "Brake";
    public const string Clutch = "Clutch";
    public const string SteeringWheelAngle = "SteeringWheelAngle";
    public const string Gear = "Gear";
    public const string Rpm = "RPM";
    public const string LatAccel = "LatAccel";
    public const string LongAccel = "LongAccel";
    public const string YawRate = "YawRate";
    public const string AbsActive = "BrakeABSactive";
    public const string OnPitRoad = "OnPitRoad";
    public const string IsOnTrack = "IsOnTrack";
    public const string TrackSurface = "PlayerTrackSurface";
    public const string IncidentCount = "PlayerCarMyIncidentCount";
    public const string Lat = "Lat";
    public const string Lon = "Lon";
}
