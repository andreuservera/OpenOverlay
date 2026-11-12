using IRacingOverlay.Sdk;

namespace IRacingOverlay.Coaching.Telemetry;

/// <summary>
/// Builds a <see cref="TelemetryFrame"/> from a live <see cref="TelemetrySnapshot"/>, so the coach can
/// subscribe to <c>IRacingConnection.TelemetryUpdated</c> without the connection knowing it exists.
/// Every read is guarded: a variable this iRacing build doesn't publish reads as 0 / false.
/// </summary>
public static class SnapshotFrames
{
    public static TelemetryFrame FromSnapshot(TelemetrySnapshot s) => new()
    {
        SessionTime = Number(s, FrameVars.SessionTime),
        SessionNum = (int)Number(s, FrameVars.SessionNum),
        SessionState = (int)Number(s, FrameVars.SessionState),
        Lap = (int)Number(s, FrameVars.Lap),
        LapDist = (float)Number(s, FrameVars.LapDist),
        LapDistPct = (float)Number(s, FrameVars.LapDistPct),
        LapLastLapTime = (float)Number(s, FrameVars.LapLastLapTime),
        Speed = (float)Number(s, FrameVars.Speed),
        Throttle = (float)Number(s, FrameVars.Throttle),
        Brake = (float)Number(s, FrameVars.Brake),
        Clutch = (float)Number(s, FrameVars.Clutch),
        SteeringWheelAngle = (float)Number(s, FrameVars.SteeringWheelAngle),
        Gear = (int)Number(s, FrameVars.Gear),
        Rpm = (float)Number(s, FrameVars.Rpm),
        LatAccel = (float)Number(s, FrameVars.LatAccel),
        LongAccel = (float)Number(s, FrameVars.LongAccel),
        YawRate = (float)Number(s, FrameVars.YawRate),
        AbsActive = Number(s, FrameVars.AbsActive) != 0,
        OnPitRoad = Number(s, FrameVars.OnPitRoad) != 0,
        IsOnTrack = !s.HasVariable(FrameVars.IsOnTrack) || Number(s, FrameVars.IsOnTrack) != 0,
        TrackSurface = s.HasVariable(FrameVars.TrackSurface) ? (int)Number(s, FrameVars.TrackSurface) : TrackSurface.OnTrack,
        IncidentCount = (int)Number(s, FrameVars.IncidentCount),
    };

    private static readonly Func<TelemetrySnapshot, string, double>[] Readers =
    [
        (s, n) => s.GetFloat(n),
        (s, n) => s.GetDouble(n),
        (s, n) => s.GetInt(n),
        (s, n) => s.GetBool(n) ? 1 : 0,
        (s, n) => s.GetBitField(n),
    ];

    // Which reader worked last time for each variable: a variable's type never changes within a
    // connection, so the try-each-type probe (TelemetrySnapshot's getters throw on a mismatch) runs
    // once per variable, not 60 times a second.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> ReaderByName = new();

    private static double Number(TelemetrySnapshot s, string name)
    {
        if (!s.HasVariable(name))
        {
            return 0;
        }

        if (ReaderByName.TryGetValue(name, out var known))
        {
            try
            {
                return Readers[known](s, name);
            }
            catch (InvalidOperationException)
            {
                ReaderByName.TryRemove(name, out _);
            }
        }

        for (var i = 0; i < Readers.Length; i++)
        {
            try
            {
                var value = Readers[i](s, name);
                ReaderByName[name] = i;
                return value;
            }
            catch (InvalidOperationException)
            {
            }
        }

        return 0;
    }
}
