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

    /// <summary>Speed 100 m after the corner: what the exit is worth on the straight.</summary>
    public required float ExitSpeedPlus100 { get; init; }

    /// <summary>Share of braking samples with ABS active, 0–1.</summary>
    public float AbsShare { get; init; }

    public required int GearAtApex { get; init; }
}

/// <summary>Measures every corner of a <see cref="CornerMap"/> on a lap.</summary>
public static class CornerAnalyzer
{
    private const float BrakeOn = 0.05f;
    private const float ThrottleOn = 0.20f;
    private const float FullThrottle = 0.95f;
    private const float TurnInShareOfPeakSteering = 0.30f;
    private const int MaxEarlyBrakeSearchM = 200;

    public static IReadOnlyList<CornerPass> Analyze(LapTrace lap, CornerMap map, int lapNumber)
    {
        var passes = new List<CornerPass>(map.Corners.Count);
        foreach (var corner in map.Corners)
        {
            if (corner.EndM >= lap.Length)
            {
                continue;
            }

            passes.Add(Measure(lap, corner, lapNumber));
        }

        return passes;
    }

    public static CornerPass Measure(LapTrace lap, Corner c, int lapNumber)
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

        var pickupFrom = release is { } rel ? rel + 1 : s;
        int? pickup = FirstIndex(pickupFrom, e, i => lap.Throttle[i] > ThrottleOn);
        int? full = pickup is { } p ? FirstIndex(p, e, i => lap.Throttle[i] >= FullThrottle) : null;

        return new CornerPass
        {
            Corner = c,
            LapNumber = lapNumber,
            SegmentTime = lap.Time[e] - lap.Time[s],
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
            ExitSpeedPlus100 = lap.Speed[Math.Min(e + 100, lap.Length - 1)],
            AbsShare = brakeSamples > 0 ? (float)absSamples / brakeSamples : 0,
            GearAtApex = lap.Gear[minAt],
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
