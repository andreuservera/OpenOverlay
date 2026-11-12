using IRacingOverlay.Coaching.Laps;

namespace IRacingOverlay.Coaching.Corners;

/// <summary>
/// One pass through one corner on one lap: its time and the measurements a coach talks about.
/// Distances are metres from the start/finish line; speeds m/s; pedals 0–1. A null distance means
/// the event didn't happen in the corner (no braking, never reached full throttle…).
/// </summary>
public sealed record CornerPass
{
    public required Corner Corner { get; init; }
    public required int LapNumber { get; init; }

    /// <summary>Time from the corner window's start to its end, seconds.</summary>
    public required double SegmentTime { get; init; }

    private readonly double? _scoreTime;

    /// <summary>
    /// What the corner is judged on: time from its start to where the next corner starts (the end of
    /// the lap for the last one), but at most <see cref="CornerAnalyzer.MaxStraightMetres"/> past the
    /// corner's end, seconds. It includes the straight after the corner, because that is where a
    /// slow exit costs its time. Equal to <see cref="SegmentTime"/> when not measured.
    /// </summary>
    public double ScoreTime
    {
        get => _scoreTime ?? SegmentTime;
        init => _scoreTime = value;
    }

    /// <summary>Where <see cref="ScoreTime"/> stops: the next corner's start, or
    /// <see cref="CornerAnalyzer.MaxStraightMetres"/> past this corner's end if that is sooner, metres.</summary>
    public int ScoreEndM { get; init; }

    /// <summary>The rest of a long straight, from <see cref="ScoreEndM"/> to the next corner's
    /// start, seconds; 0 when the next corner comes sooner. It is its own piece of the theoretical
    /// best, so an off on a long straight (or in a flat-out kink like Blanchimont) doesn't void the
    /// corner before it.</summary>
    public double RestOfStraightTime { get; init; }

    /// <summary>True when the rest of the straight had no off-track, pit road or incident.</summary>
    public bool RestOfStraightClean { get; init; } = true;

    /// <summary>Distance the car actually travelled through the corner window (speed × time) over
    /// the window's length along the track. Below the usual value for this corner means a shorter
    /// path than the track allows: a cut.</summary>
    public double PathRatio { get; init; } = 1;

    /// <summary>Why this pass can't be a reference; <see cref="CornerPassFlaw.None"/> when it can.</summary>
    public CornerPassFlaw Flaws { get; init; }

    /// <summary>True when the pass is clean enough to be the best pass or count toward the
    /// theoretical best.</summary>
    public bool IsClean => Flaws == CornerPassFlaw.None;

    public required float EntrySpeed { get; init; }

    /// <summary>First point with the brake on.</summary>
    public int? BrakePointM { get; init; }

    public float PeakBrake { get; init; }

    /// <summary>Metres from the brake point to 90% of peak pressure: short = a firm initial hit.</summary>
    public int? BrakeRampM { get; init; }

    /// <summary>Where the brake came off after the braking zone.</summary>
    public int? BrakeReleaseM { get; init; }

    /// <summary>First point with real steering input (30% of the corner's peak).</summary>
    public int? TurnInM { get; init; }

    /// <summary>How far the brake was carried past turn-in, metres (0 = released before turning).</summary>
    public int TrailBrakeM { get; init; }

    public required float MinSpeed { get; init; }
    public required int MinSpeedAtM { get; init; }

    /// <summary>First throttle after the brake came off (above 20%).</summary>
    public int? ThrottlePickupM { get; init; }

    /// <summary>Back to full throttle.</summary>
    public int? FullThrottleM { get; init; }

    public required float ExitSpeed { get; init; }

    /// <summary>Speed 100 m after the corner, or where the next corner begins if that is sooner:
    /// what the exit is worth on the straight.</summary>
    public required float ExitSpeedPlus100 { get; init; }

    /// <summary>Share of braking samples with ABS active, 0–1.</summary>
    public float AbsShare { get; init; }

    public required int GearAtApex { get; init; }

    /// <summary>Which way the corner turns, from the steering through it: +1 left, -1 right, 0 when
    /// it can't tell (a chicane steered both ways about equally).</summary>
    public int Direction { get; init; }
}

/// <summary>Measures every corner of a <see cref="CornerMap"/> on a lap.</summary>
public static class CornerAnalyzer
{
    private const float BrakeOn = 0.05f;
    private const float ThrottleOn = 0.20f;
    private const float FullThrottle = 0.95f;
    private const float TurnInShareOfPeakSteering = 0.30f;
    private const int MaxEarlyBrakeSearchM = 200;

    /// <summary>How much of the straight after a corner counts toward its score, metres. A slow exit
    /// costs most of its time in the first few hundred metres; further on, the straight is a
    /// separate piece, so validity stays local on long laps like Spa.</summary>
    public const int MaxStraightMetres = 400;

    /// <param name="lapFlaws">The lap's own flaws: on a race start or out lap
    /// (<see cref="LapFlaw.StartLap"/>) the first corners are marked, since they are driven from
    /// low speed, in traffic or on cold tyres.</param>
    public static IReadOnlyList<CornerPass> Analyze(LapTrace lap, CornerMap map, int lapNumber, LapFlaw lapFlaws = LapFlaw.None)
    {
        var passes = new List<CornerPass>(map.Corners.Count);
        for (var i = 0; i < map.Corners.Count; i++)
        {
            var corner = map.Corners[i];
            if (corner.EndM >= lap.Length)
            {
                continue;
            }

            int? nextStart = i + 1 < map.Corners.Count ? map.Corners[i + 1].StartM : null;
            var pass = Measure(lap, corner, lapNumber, nextStart);
            var extra = CornerPassFlaw.None;
            if (lapFlaws.HasFlag(LapFlaw.StartLap) && i < CornerChecks.StartLapCorners)
            {
                extra |= CornerPassFlaw.StartLap;
            }

            if (CornerChecks.CornersSincePitExit(lap, map, i) is { } since && since < CornerChecks.StartLapCorners)
            {
                extra |= CornerPassFlaw.PitExit;
            }

            // Running wide into the next corner's gravel usually means this corner's straight was
            // gained by braking too late for it.
            if (i + 1 < map.Corners.Count && CornerChecks.OffOnEntry(lap, map.Corners[i + 1]))
            {
                extra |= CornerPassFlaw.OffTrackNext;
            }

            passes.Add(extra == CornerPassFlaw.None ? pass : pass with { Flaws = pass.Flaws | extra });
        }

        return passes;
    }

    /// <param name="nextCornerStartM">Where the next corner begins, so the "+100 m" exit speed is
    /// never read inside it.</param>
    public static CornerPass Measure(LapTrace lap, Corner c, int lapNumber, int? nextCornerStartM = null)
    {
        var s = c.StartM;
        var e = c.EndM;

        var minAt = s;
        for (var i = s; i <= e; i++)
        {
            if (lap.Speed[i] < lap.Speed[minAt])
            {
                minAt = i;
            }
        }

        // Braking: first brake in the entry half (start to the slowest point). The window comes from
        // the reference lap, so on a lap braked earlier the brake is already on at its start: follow
        // it back to where it really began.
        int? brakePoint = FirstIndex(s, minAt, i => lap.Brake[i] > BrakeOn);
        if (brakePoint == s)
        {
            var back = s;
            while (back > Math.Max(0, s - MaxEarlyBrakeSearchM) && lap.Brake[back - 1] > BrakeOn)
            {
                back--;
            }

            brakePoint = back;
        }

        float peak = 0;
        int? ramp = null;
        int? release = null;
        var absSamples = 0;
        var brakeSamples = 0;
        if (brakePoint is { } bp)
        {
            var i = bp;
            while (i <= e && lap.Brake[i] > BrakeOn)
            {
                peak = Math.Max(peak, lap.Brake[i]);
                brakeSamples++;
                if (lap.Abs[i])
                {
                    absSamples++;
                }

                i++;
            }

            release = i - 1;
            var target = 0.9f * peak;
            ramp = FirstIndex(bp, release.Value, j => lap.Brake[j] >= target) - bp;
        }

        var peakSteer = 0f;
        for (var i = s; i <= e; i++)
        {
            peakSteer = Math.Max(peakSteer, Math.Abs(lap.Steering[i]));
        }

        int? turnIn = peakSteer > 0
            ? FirstIndex(s, minAt, i => Math.Abs(lap.Steering[i]) >= TurnInShareOfPeakSteering * peakSteer)
            : null;
        var trail = release is { } r && turnIn is { } ti ? Math.Max(0, r - ti) : 0;

        // iRacing's steering angle is positive to the left. Weigh the whole window so a chicane
        // takes the direction of its bigger part.
        double steerSum = 0, steerAbs = 0;
        for (var i = s; i <= e; i++)
        {
            steerSum += lap.Steering[i];
            steerAbs += Math.Abs(lap.Steering[i]);
        }

        var direction = steerAbs > 0 && Math.Abs(steerSum) > 0.3 * steerAbs ? Math.Sign(steerSum) : 0;

        var pickupFrom = release is { } rel ? rel + 1 : s;
        int? pickup = FirstIndex(pickupFrom, e, i => lap.Throttle[i] > ThrottleOn);
        int? full = pickup is { } p ? FirstIndex(p, e, i => lap.Throttle[i] >= FullThrottle) : null;

        var straightEnd = Math.Clamp(nextCornerStartM ?? lap.Length - 1, e, lap.Length - 1);
        var scoreEnd = Math.Min(straightEnd, e + MaxStraightMetres);

        return new CornerPass
        {
            Corner = c,
            LapNumber = lapNumber,
            SegmentTime = lap.Time[e] - lap.Time[s],
            ScoreTime = lap.Time[scoreEnd] - lap.Time[s],
            ScoreEndM = scoreEnd,
            RestOfStraightTime = lap.Time[straightEnd] - lap.Time[scoreEnd],
            RestOfStraightClean = CornerChecks.StretchIsClean(lap, scoreEnd, straightEnd),
            PathRatio = CornerChecks.PathRatio(lap, s, e),
            Flaws = CornerChecks.Inspect(lap, s, scoreEnd),
            EntrySpeed = lap.Speed[s],
            BrakePointM = brakePoint,
            PeakBrake = peak,
            BrakeRampM = ramp,
            BrakeReleaseM = release,
            TurnInM = turnIn,
            TrailBrakeM = trail,
            MinSpeed = lap.Speed[minAt],
            MinSpeedAtM = minAt,
            ThrottlePickupM = pickup,
            FullThrottleM = full,
            ExitSpeed = lap.Speed[e],
            ExitSpeedPlus100 = lap.Speed[Math.Clamp(Math.Min(e + 100, nextCornerStartM ?? int.MaxValue), e, lap.Length - 1)],
            AbsShare = brakeSamples > 0 ? (float)absSamples / brakeSamples : 0,
            GearAtApex = lap.Gear[minAt],
            Direction = direction,
        };
    }

    private static int? FirstIndex(int from, int to, Func<int, bool> test)
    {
        for (var i = from; i <= to; i++)
        {
            if (test(i))
            {
                return i;
            }
        }

        return null;
    }
}
