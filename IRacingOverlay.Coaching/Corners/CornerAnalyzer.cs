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

    /// <summary>Where <see cref="ScoreTime"/> starts: a little before the braking zone (see
    /// <see cref="CornerAnalyzer.PieceStart"/>), metres.</summary>
    public int ScoreStartM { get; init; }

    /// <summary>Where <see cref="ScoreTime"/> stops: the next corner's start, or
    /// <see cref="CornerAnalyzer.MaxStraightMetres"/> past this corner's end if that is sooner, metres.</summary>
    public int ScoreEndM { get; init; }

    /// <summary>The rest of a long straight, from <see cref="ScoreEndM"/> to the next corner's
    /// start, seconds; 0 when the next corner comes sooner. It is its own piece of the theoretical
    /// best, so an off on a long straight (or in a flat-out kink like Blanchimont) doesn't void the
    /// corner before it.</summary>
    public double RestOfStraightTime { get; init; }

    /// <summary>What voids the rest of the straight (off-track, pit road, incident); none when clean.</summary>
    public CornerPassFlaw RestOfStraightFlaws { get; init; }

    /// <summary>True when the rest of the straight had no off-track, pit road or incident.</summary>
    public bool RestOfStraightClean => RestOfStraightFlaws == CornerPassFlaw.None;

    /// <summary>True when there is a rest of the straight at all (the next corner is more than
    /// <see cref="CornerAnalyzer.MaxStraightMetres"/> past this one).</summary>
    public bool HasRestOfStraight { get; init; }

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

    /// <summary>First point off full throttle (below 95%) on the way in; null when the throttle
    /// never came off before the slowest point (a flat corner).</summary>
    public int? LiftPointM { get; init; }

    /// <summary>Metres from <see cref="LiftPointM"/> back to full throttle (or the window's end):
    /// how long the car was off full throttle. 0 when flat.</summary>
    public int LiftLengthM { get; init; }

    /// <summary>The throttle was already off where this corner's piece starts: the lift began in the
    /// corner before and never went back to full in between. <see cref="LiftPointM"/> is then the
    /// piece start, not a lift point the driver chose for this corner.</summary>
    public bool LiftCarriedOver { get; init; }

    /// <summary>Lowest throttle before the brake or the slowest point: how deep the lift went, 0–1.
    /// 1 when flat; 0 for a full lift (and for every braking corner).</summary>
    public float MinThrottle { get; init; } = 1;

    /// <summary>Metres the brake was on, from the brake point to its release; 0 when no braking.</summary>
    public int BrakeLengthM { get; init; }

    /// <summary>The longest stretch with neither pedal really pressed (throttle under 10%, no
    /// brake), metres: lost time in almost every corner.</summary>
    public int CoastingM { get; init; }

    /// <summary>How many times the throttle came back off by 20% or more between pick-up and full
    /// throttle: the rear stepping out, or a line that ran out of road.</summary>
    public int ThrottleLifts { get; init; }

    /// <summary>How many separate slow points the window has (speed dropping, rising by a clear
    /// margin, dropping again): 2 or more for a chicane or esses.</summary>
    public int SpeedDips { get; init; } = 1;

    /// <summary>Steering per lateral g around the slowest point, rad per m/s²; null when the car was
    /// hardly cornering there. More than the best pass for the same grip means understeer.</summary>
    public double? SteerPerLatAccel { get; init; }
}

/// <summary>Measures every corner of a <see cref="CornerMap"/> on a lap.</summary>
public static class CornerAnalyzer
{
    private const float BrakeOn = 0.05f;
    private const float ThrottleOn = 0.20f;
    private const float FullThrottle = 0.95f;
    private const float TurnInShareOfPeakSteering = 0.30f;
    private const int MaxEarlyBrakeSearchM = 200;
    private const float CoastThrottle = 0.10f;
    private const float ThrottleLiftDrop = 0.20f;
    private const int ApexBalanceM = 20;
    private const float MinLatAccelForBalance = 3f;

    /// <summary>How much of the straight after a corner counts toward its score, metres. A slow exit
    /// costs most of its time in the first few hundred metres; further on, the straight is a
    /// separate piece, so validity stays local on long laps like Spa.</summary>
    public const int MaxStraightMetres = 400;

    /// <summary>A corner's piece of the lap starts this far before its window (where braking or
    /// lifting began), metres. Splitting the lap right at the braking point would hand a late
    /// braker the gain on one side and the loss on the other, and the theoretical best would keep
    /// only the gain.</summary>
    public const int LeadInMetres = 100;

    /// <summary>
    /// Where corner <paramref name="index"/>'s piece of the lap starts: <see cref="LeadInMetres"/>
    /// before its window, but never past halfway back to the previous corner's end, so on a short
    /// straight the boundary sits where every lap is still accelerating.
    /// </summary>
    public static int PieceStart(CornerMap map, int index)
    {
        var c = map.Corners[index];
        var prevEnd = index > 0 ? map.Corners[index - 1].EndM : 0;
        return Math.Clamp(Math.Max(c.StartM - LeadInMetres, (prevEnd + c.StartM) / 2), 0, c.StartM);
    }

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
            int? nextPiece = i + 1 < map.Corners.Count ? PieceStart(map, i + 1) : null;
            var pass = Measure(lap, corner, lapNumber, nextStart, PieceStart(map, i), nextPiece);
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
    /// <param name="pieceStartM">Where the corner's score starts; its window's start when null.</param>
    /// <param name="nextPieceStartM">Where the next corner's score starts; the next corner's start
    /// (or the end of the lap) when null.</param>
    public static CornerPass Measure(LapTrace lap, Corner c, int lapNumber, int? nextCornerStartM = null, int? pieceStartM = null, int? nextPieceStartM = null)
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

        // Lift: first throttle off on the way in, followed back like the brake when it was already off.
        int? lift = FirstIndex(s, minAt, i => lap.Throttle[i] < FullThrottle);
        // Followed back no further than this corner's piece: a lift that carries on from the corner
        // before belongs to that corner, not to this one.
        var liftCarriedOver = false;
        if (lift == s)
        {
            var limit = Math.Max(0, s - MaxEarlyBrakeSearchM);
            if (pieceStartM is { } ps && ps < s)
            {
                limit = Math.Max(limit, ps);
            }

            var back = s;
            while (back > limit && lap.Throttle[back - 1] < FullThrottle)
            {
                back--;
            }

            liftCarriedOver = pieceStartM is not null && back == limit && back > 0 && lap.Throttle[back - 1] < FullThrottle;
            lift = back;
        }

        var liftEnd = brakePoint ?? minAt;
        var minThrottle = 1f;
        if (lift is { } l0)
        {
            for (var i = l0; i <= Math.Max(l0, liftEnd); i++)
            {
                minThrottle = Math.Min(minThrottle, lap.Throttle[i]);
            }
        }

        // Pick-up is searched after the brake, or for a lift-only corner after the deepest point of
        // the lift (the throttle is still on at the window's start, which isn't a pick-up).
        var pickupFrom = release is { } rel ? rel + 1 : s;
        if (release is null && lift is { } lf)
        {
            // The lift may have begun in the corner before; the pick-up is still this corner's.
            pickupFrom = Math.Max(lf, s);
            for (var i = pickupFrom; i <= minAt; i++)
            {
                if (lap.Throttle[i] < lap.Throttle[pickupFrom])
                {
                    pickupFrom = i;
                }
            }
        }

        int? pickup = FirstIndex(pickupFrom, e, i => lap.Throttle[i] > ThrottleOn);
        int? full = pickup is { } p ? FirstIndex(p, e, i => lap.Throttle[i] >= FullThrottle) : null;


        var liftLength = lift is { } l1 ? Math.Max(0, (full ?? e) - l1) : 0;

        int coastRun = 0, coastBest = 0;
        for (var i = s; i <= e; i++)
        {
            coastRun = lap.Throttle[i] < CoastThrottle && lap.Brake[i] <= BrakeOn ? coastRun + 1 : 0;
            coastBest = Math.Max(coastBest, coastRun);
        }

        var throttleLifts = 0;
        if (pickup is { } p0)
        {
            var high = lap.Throttle[p0];
            var dropped = false;
            for (var i = p0; i <= (full ?? e); i++)
            {
                if (!dropped && lap.Throttle[i] < high - ThrottleLiftDrop)
                {
                    throttleLifts++;
                    dropped = true;
                    high = lap.Throttle[i];
                }
                else if (lap.Throttle[i] > high)
                {
                    high = lap.Throttle[i];
                    dropped = false;
                }
            }
        }

        double steerSumApex = 0, latSumApex = 0;
        for (var i = Math.Max(s, minAt - ApexBalanceM); i <= Math.Min(e, minAt + ApexBalanceM); i++)
        {
            steerSumApex += Math.Abs(lap.Steering[i]);
            latSumApex += Math.Abs(lap.LatAccel[i]);
        }

        var apexSamples = Math.Min(e, minAt + ApexBalanceM) - Math.Max(s, minAt - ApexBalanceM) + 1;
        double? steerPerG = latSumApex / apexSamples >= MinLatAccelForBalance ? steerSumApex / latSumApex : null;

        var pieceStart = Math.Clamp(pieceStartM ?? s, 0, s);
        var straightEnd = Math.Clamp(nextPieceStartM ?? nextCornerStartM ?? lap.Length - 1, e, lap.Length - 1);
        var scoreEnd = Math.Min(straightEnd, e + MaxStraightMetres);

        return new CornerPass
        {
            Corner = c,
            LapNumber = lapNumber,
            SegmentTime = lap.Time[e] - lap.Time[s],
            ScoreTime = lap.Time[scoreEnd] - lap.Time[pieceStart],
            ScoreStartM = pieceStart,
            ScoreEndM = scoreEnd,
            RestOfStraightTime = lap.Time[straightEnd] - lap.Time[scoreEnd],
            RestOfStraightFlaws = straightEnd > scoreEnd ? CornerChecks.StretchFlaws(lap, scoreEnd, straightEnd) : CornerPassFlaw.None,
            HasRestOfStraight = straightEnd > scoreEnd,
            PathRatio = CornerChecks.PathRatio(lap, s, e),
            Flaws = CornerChecks.Inspect(lap, pieceStart, scoreEnd),
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
            LiftPointM = lift,
            LiftLengthM = liftLength,
            LiftCarriedOver = liftCarriedOver,
            MinThrottle = lift is null ? 1 : minThrottle,
            BrakeLengthM = brakePoint is { } b0 && release is { } r0 ? r0 - b0 + 1 : 0,
            CoastingM = coastBest,
            ThrottleLifts = throttleLifts,
            SpeedDips = SpeedDips(lap, s, e),
            SteerPerLatAccel = steerPerG,
        };
    }

    /// <summary>Separate slow points in the window: a dip only ends once speed has risen by 1 m/s
    /// and 3% again, so noise and a short breath between two parts of one corner don't count.</summary>
    private static int SpeedDips(LapTrace lap, int s, int e)
    {
        var dips = 0;
        var falling = false;
        var hi = lap.Speed[s];
        var lo = lap.Speed[s];
        for (var i = s + 1; i <= e; i++)
        {
            var v = lap.Speed[i];
            if (!falling)
            {
                hi = Math.Max(hi, v);
                if (hi - v >= Math.Max(1f, 0.03f * hi))
                {
                    falling = true;
                    lo = v;
                }
            }
            else
            {
                lo = Math.Min(lo, v);
                if (v - lo >= Math.Max(1f, 0.03f * lo))
                {
                    dips++;
                    falling = false;
                    hi = v;
                }
            }
        }

        return Math.Max(1, falling ? dips + 1 : dips);
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
