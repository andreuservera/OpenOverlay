using System.Text;
using IRacingOverlay.Coaching.Telemetry;

namespace IRacingOverlay.Coaching.Tests.Support;

/// <summary>
/// Writes a minimal but layout-faithful .ibt: irsdk header, disk sub-header, variable headers,
/// session YAML, then one record per frame — the same structure as the real files the reader was
/// checked against.
/// </summary>
internal static class IbtWriter
{
    private enum T { Bool = 1, Int = 2, Float = 4, Double = 5 }

    private static readonly (string Name, T Type, Func<TelemetryFrame, double> Get)[] Vars =
    [
        ("SessionTime", T.Double, f => f.SessionTime),
        ("SessionNum", T.Int, f => f.SessionNum),
        ("SessionState", T.Int, f => f.SessionState),
        ("Lap", T.Int, f => f.Lap),
        ("LapDist", T.Float, f => f.LapDist),
        ("LapDistPct", T.Float, f => f.LapDistPct),
        ("LapLastLapTime", T.Float, f => f.LapLastLapTime),
        ("Speed", T.Float, f => f.Speed),
        ("Throttle", T.Float, f => f.Throttle),
        ("Brake", T.Float, f => f.Brake),
        ("SteeringWheelAngle", T.Float, f => f.SteeringWheelAngle),
        ("Gear", T.Int, f => f.Gear),
        ("LatAccel", T.Float, f => f.LatAccel),
        ("LongAccel", T.Float, f => f.LongAccel),
        ("YawRate", T.Float, f => f.YawRate),
        ("BrakeABSactive", T.Bool, f => f.AbsActive ? 1 : 0),
        ("OnPitRoad", T.Bool, f => f.OnPitRoad ? 1 : 0),
        ("IsOnTrack", T.Bool, f => f.IsOnTrack ? 1 : 0),
        ("PlayerTrackSurface", T.Int, f => f.TrackSurface),
        ("PlayerCarMyIncidentCount", T.Int, f => f.IncidentCount),
        ("Lat", T.Double, f => f.Latitude ?? 0),
        ("Lon", T.Double, f => f.Longitude ?? 0),
    ];

    public const string Yaml = "---\nWeekendInfo:\n TrackName: testtrack\n TrackDisplayName: Test Track\n TrackLength: 2.27 km\nDriverInfo:\n DriverCarIdx: 0\n Drivers:\n - CarIdx: 0\n   UserName: Test Driver\n   CarScreenName: Test Car\n...\n";

    public static byte[] Write(IReadOnlyList<TelemetryFrame> frames, int? declaredRecords = null)
    {
        var offsets = new int[Vars.Length];
        var bufLen = 0;
        for (var i = 0; i < Vars.Length; i++)
        {
            offsets[i] = bufLen;
            bufLen += Vars[i].Type switch { T.Bool => 1, T.Double => 8, _ => 4 };
        }

        var varHeaderOffset = 144;
        var yamlBytes = Encoding.ASCII.GetBytes(Yaml);
        var yamlOffset = varHeaderOffset + Vars.Length * 144;
        var dataOffset = yamlOffset + yamlBytes.Length + 16;

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(2); w.Write(1); w.Write(60); w.Write(0);              // ver, status, tickRate, sessionInfoUpdate
        w.Write(yamlBytes.Length); w.Write(yamlOffset);               // sessionInfoLen, sessionInfoOffset
        w.Write(Vars.Length); w.Write(varHeaderOffset);               // numVars, varHeaderOffset
        w.Write(1); w.Write(bufLen); w.Write(0L);                     // numBuf, bufLen, pad
        w.Write(frames.Count); w.Write(dataOffset); w.Write(0L);      // varBuf[0]: tickCount, bufOffset, pad
        w.Write(new byte[112 - (int)ms.Position]);                    // varBuf[1..3]

        w.Write(1_700_000_000L);                                      // disk sub-header
        w.Write(frames.Count > 0 ? frames[0].SessionTime : 0);
        w.Write(frames.Count > 0 ? frames[^1].SessionTime : 0);
        w.Write(frames.Count > 0 ? frames[^1].Lap : 0);
        w.Write(declaredRecords ?? frames.Count);

        for (var i = 0; i < Vars.Length; i++)
        {
            var entry = new byte[144];
            BitConverter.GetBytes((int)Vars[i].Type).CopyTo(entry, 0);
            BitConverter.GetBytes(offsets[i]).CopyTo(entry, 4);
            BitConverter.GetBytes(1).CopyTo(entry, 8);
            Encoding.ASCII.GetBytes(Vars[i].Name).CopyTo(entry, 16);
            w.Write(entry);
        }

        w.Write(yamlBytes);
        w.Write(new byte[16]);

        foreach (var f in frames)
        {
            var record = new byte[bufLen];
            for (var i = 0; i < Vars.Length; i++)
            {
                var value = Vars[i].Get(f);
                var bytes = Vars[i].Type switch
                {
                    T.Bool => [(byte)(value != 0 ? 1 : 0)],
                    T.Int => BitConverter.GetBytes((int)value),
                    T.Float => BitConverter.GetBytes((float)value),
                    _ => BitConverter.GetBytes(value),
                };
                bytes.CopyTo(record, offsets[i]);
            }

            w.Write(record);
        }

        return ms.ToArray();
    }
}
