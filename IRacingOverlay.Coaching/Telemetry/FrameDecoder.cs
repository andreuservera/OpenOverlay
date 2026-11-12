using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Coaching.Telemetry;

/// <summary>
/// Reads a <see cref="TelemetryFrame"/> straight out of one raw irsdk tick buffer, using the variable
/// layout parsed by <see cref="IrsdkParser"/>. Used for .ibt records, which share the shared-memory
/// layout byte for byte. A variable missing from the file reads as 0 / false / null.
/// </summary>
internal sealed class FrameDecoder
{
    private readonly IReadOnlyDictionary<string, IrsdkVarHeader> _vars;

    public FrameDecoder(IReadOnlyDictionary<string, IrsdkVarHeader> vars)
    {
        _vars = vars;
    }

    public TelemetryFrame Decode(ReadOnlySpan<byte> record) => new()
    {
        SessionTime = Number(record, FrameVars.SessionTime),
        SessionNum = (int)Number(record, FrameVars.SessionNum),
        SessionState = (int)Number(record, FrameVars.SessionState),
        Lap = (int)Number(record, FrameVars.Lap),
        LapDist = (float)Number(record, FrameVars.LapDist),
        LapDistPct = (float)Number(record, FrameVars.LapDistPct),
        LapLastLapTime = (float)Number(record, FrameVars.LapLastLapTime),
        Speed = (float)Number(record, FrameVars.Speed),
        Throttle = (float)Number(record, FrameVars.Throttle),
        Brake = (float)Number(record, FrameVars.Brake),
        Clutch = (float)Number(record, FrameVars.Clutch),
        SteeringWheelAngle = (float)Number(record, FrameVars.SteeringWheelAngle),
        Gear = (int)Number(record, FrameVars.Gear),
        Rpm = (float)Number(record, FrameVars.Rpm),
        LatAccel = (float)Number(record, FrameVars.LatAccel),
        LongAccel = (float)Number(record, FrameVars.LongAccel),
        YawRate = (float)Number(record, FrameVars.YawRate),
        AbsActive = Number(record, FrameVars.AbsActive) != 0,
        OnPitRoad = Number(record, FrameVars.OnPitRoad) != 0,
        // Absent means "driving": better to keep a real lap than to throw it away.
        IsOnTrack = !_vars.ContainsKey(FrameVars.IsOnTrack) || Number(record, FrameVars.IsOnTrack) != 0,
        TrackSurface = _vars.ContainsKey(FrameVars.TrackSurface) ? (int)Number(record, FrameVars.TrackSurface) : TrackSurface.OnTrack,
        IncidentCount = (int)Number(record, FrameVars.IncidentCount),
        Latitude = _vars.ContainsKey(FrameVars.Lat) ? Number(record, FrameVars.Lat) : null,
        Longitude = _vars.ContainsKey(FrameVars.Lon) ? Number(record, FrameVars.Lon) : null,
    };

    /// <summary>The first element of a variable as a double, whatever its declared type (iRacing's
    /// declared types are not always what its docs say, so every read is type-tolerant).</summary>
    public double Number(ReadOnlySpan<byte> record, string name)
    {
        if (!_vars.TryGetValue(name, out var header))
        {
            return 0;
        }

        return Read(record, header, 0);
    }

    internal static double Read(ReadOnlySpan<byte> record, IrsdkVarHeader header, int index)
    {
        var offset = header.Offset;
        return header.Type switch
        {
            IrsdkVarType.Char or IrsdkVarType.Bool => record[offset + index],
            IrsdkVarType.Int => BitConverter.ToInt32(record.Slice(offset + 4 * index, 4)),
            IrsdkVarType.BitField => BitConverter.ToUInt32(record.Slice(offset + 4 * index, 4)),
            IrsdkVarType.Float => BitConverter.ToSingle(record.Slice(offset + 4 * index, 4)),
            IrsdkVarType.Double => BitConverter.ToDouble(record.Slice(offset + 8 * index, 8)),
            _ => 0,
        };
    }
}
