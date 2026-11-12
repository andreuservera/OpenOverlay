using IRacingOverlay.Coaching.Telemetry;

namespace IRacingOverlay.Coaching.Tests.Support;

/// <summary>One stretch of a synthetic track: straight when <see cref="Radius"/> is 0.</summary>
internal sealed record TrackPiece(double Length, double Radius = 0);

/// <summary>
/// A point-mass lap simulator for tests: each metre's speed is limited by grip in corners
/// (v = √(a_lat · R)), by acceleration out of them and by braking into them, and the driver's inputs
/// follow from that (brake while decelerating, full throttle while accelerating, part throttle at the
/// grip limit). Frames are then sampled at 60 Hz like iRacing's, so laps can be generated with exactly
/// known lap times, braking points and corner speeds.
/// </summary>
internal sealed class TrackSimulator
{
    public const double LateralGrip = 15;   // m/s²
    public const double Acceleration = 5;   // m/s²
    public const double Braking = 14;       // m/s²
    public const double TopSpeed = 70;      // m/s
    public const double TickRate = 60;

    private readonly double[] _curvature;
    private readonly int[] _pieceAt;

    public TrackSimulator(params TrackPiece[] pieces)
    {
        Pieces = pieces;
        Length = (int)Math.Round(pieces.Sum(p => p.Length));
        _curvature = new double[Length + 1];
        _pieceAt = new int[Length + 1];
        var d = 0;
        for (var i = 0; i < pieces.Length; i++)
        {
            var end = (int)Math.Round(pieces.Take(i + 1).Sum(p => p.Length));
            for (; d < end; d++)
            {
                _curvature[d] = pieces[i].Radius > 0 ? 1 / pieces[i].Radius : 0;
                _pieceAt[d] = i;
            }
        }

        _curvature[Length] = _curvature[0];
        _pieceAt[Length] = _pieceAt[0];
    }

    /// <summary>Hairpin, medium corner, flat kink, tight corner; the line in a long straight.</summary>
    public static TrackSimulator Standard() => new(
        new TrackPiece(300),
        new TrackPiece(60, 30),    // piece 1: hairpin
        new TrackPiece(400),
        new TrackPiece(120, 80),   // piece 3: medium
        new TrackPiece(250),
        new TrackPiece(150, 600),  // piece 5: flat kink
        new TrackPiece(300),
        new TrackPiece(90, 50),    // piece 7: tight
        new TrackPiece(600));

    public IReadOnlyList<TrackPiece> Pieces { get; }

    public int Length { get; }

    /// <summary>Start metre of a piece.</summary>
    public int PieceStart(int piece) => (int)Math.Round(Pieces.Take(piece).Sum(p => p.Length));

    /// <summary>Speed (m/s) per metre for one lap. <paramref name="cornerSpeed"/> scales the grip limit of
    /// chosen pieces (0.95 = that corner taken 5% slower), to make one lap worse in one place.</summary>
    public double[] SpeedProfile(IReadOnlyDictionary<int, double>? cornerSpeed = null)
    {
        var n = Length;
        var limit = new double[n];
        for (var d = 0; d < n; d++)
        {
            var k = _curvature[d];
            var v = k > 0 ? Math.Min(TopSpeed, Math.Sqrt(LateralGrip / k)) : TopSpeed;
            if (cornerSpeed is not null && cornerSpeed.TryGetValue(_pieceAt[d], out var f))
            {
                v *= f;
            }

            limit[d] = v;
        }

        // Three laps back to back so the profile wraps around the line, then keep the middle one.
        var m = 3 * n;
        var v3 = new double[m];
        for (var i = 0; i < m; i++)
        {
            v3[i] = limit[i % n];
        }

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 1; i < m; i++)
            {
                v3[i] = Math.Min(v3[i], Math.Sqrt(v3[i - 1] * v3[i - 1] + 2 * Acceleration));
            }

            for (var i = m - 2; i >= 0; i--)
            {
                v3[i] = Math.Min(v3[i], Math.Sqrt(v3[i + 1] * v3[i + 1] + 2 * Braking));
            }
        }

        return v3[n..(2 * n)];
    }

    /// <summary>Seconds to drive one lap at this profile (trapezoidal per metre).</summary>
    public static double LapTime(double[] v)
    {
        var t = 0.0;
        for (var d = 0; d < v.Length; d++)
        {
            t += 2.0 / (v[d] + v[(d + 1) % v.Length]);
        }

        return t;
    }

    /// <summary>
    /// 60 Hz frames for <paramref name="given"/> profiles driven back to back, starting at
    /// <paramref name="startPct"/> of lap 0 (so lap 0 is partial) and running 10% into the lap after
    /// the last one, so every lap from 1 on is completed. Lap numbers start at 0.
    /// </summary>
    public List<TelemetryFrame> Drive(IReadOnlyList<double[]> given, double startPct = 0.5, double startTime = 100)
    {
        var n = Length;
        var laps = given.Append(given[^1]).ToList();
        var stopMetre = given.Count * n + 0.1 * n;

        // Global distance → time table across all laps.
        var totalMetres = laps.Count * n;
        var time = new double[totalMetres + 1];
        var speed = new double[totalMetres + 1];
        for (var g = 0; g < totalMetres; g++)
        {
            var v = laps[g / n];
            var d = g % n;
            speed[g] = v[d];
            var next = d + 1 < n ? v[d + 1] : laps[Math.Min(g / n + 1, laps.Count - 1)][0];
            time[g + 1] = time[g] + 2.0 / (v[d] + next);
        }

        speed[totalMetres] = laps[^1][0];

        var frames = new List<TelemetryFrame>();
        var startMetre = startPct * n;
        var t0 = Interp(time, startMetre);
        var j = (int)startMetre;
        for (var tick = 0; ; tick++)
        {
            var t = t0 + tick / TickRate;
            while (j < totalMetres && time[j + 1] < t)
            {
                j++;
            }

            if (j >= stopMetre)
            {
                break;
            }

            var f = (t - time[j]) / (time[j + 1] - time[j]);
            var g = j + f;
            var lap = j / n;
            var dist = g - lap * n;
            var d = j % n;
            var v = speed[j] + (speed[j + 1] - speed[j]) * f;
            var accel = (speed[j + 1] * speed[j + 1] - speed[j] * speed[j]) / 2;
            var k = _curvature[d];

            float throttle, brake;
            if (accel < -0.5)
            {
                throttle = 0;
                brake = (float)Math.Min(1, -accel / Braking);
            }
            else if (accel > 0.5 || k == 0 || v >= TopSpeed - 0.01)
            {
                throttle = 1;
                brake = 0;
            }
            else
            {
                throttle = 0.4f;
                brake = 0;
            }

            frames.Add(new TelemetryFrame
            {
                SessionTime = startTime + (t - t0),
                SessionNum = 0,
                SessionState = 4,
                Lap = lap,
                LapDist = (float)dist,
                LapDistPct = (float)(dist / n),
                Speed = (float)v,
                Throttle = throttle,
                Brake = brake,
                SteeringWheelAngle = (float)(k * 15),
                Gear = Math.Clamp((int)(v / 12) + 1, 1, 6),
                LatAccel = (float)(v * v * k),
                LongAccel = (float)accel,
                YawRate = (float)(v * k),
                IsOnTrack = true,
                TrackSurface = TrackSurface.OnTrack,
            });
        }

        return frames;
    }

    private static double Interp(double[] values, double at)
    {
        var i = (int)at;
        return values[i] + (values[Math.Min(i + 1, values.Length - 1)] - values[i]) * (at - i);
    }
}
