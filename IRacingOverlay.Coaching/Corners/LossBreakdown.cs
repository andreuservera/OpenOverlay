using IRacingOverlay.Coaching.Laps;

namespace IRacingOverlay.Coaching.Corners;

/// <summary>The phases of a corner's piece of the lap, in driving order.</summary>
public enum CornerPhase
{
    /// <summary>From the start of the piece to where the best pass braked (or lifted).</summary>
    Approach,

    /// <summary>The best pass's braking: brake point to release.</summary>
    Braking,

    /// <summary>Brake release to throttle pick-up: turning the car, carrying speed to the apex.</summary>
    Rotation,

    /// <summary>Throttle pick-up to full throttle: putting the power down.</summary>
    Traction,

    /// <summary>Full throttle to the end of the piece: what the exit is worth on the straight.</summary>
    Straight,
}

/// <summary>Time lost in each phase of a corner against the best pass, seconds (negative = gained).</summary>
public sealed record PhaseLoss(double Approach, double Braking, double Rotation, double Traction, double Straight)
{
    public double Total => Approach + Braking + Rotation + Traction + Straight;

    public double this[CornerPhase phase] => phase switch
    {
        CornerPhase.Approach => Approach,
        CornerPhase.Braking => Braking,
        CornerPhase.Rotation => Rotation,
        CornerPhase.Traction => Traction,
        _ => Straight,
    };
}

/// <summary>
/// Where a corner's time went: the time delta to the best pass, split at the best pass's own
/// brake point, release, throttle pick-up and full throttle. Both laps are on the same 1 m grid, so
/// the time each took between two distances is compared directly; a loss that only shows on the
/// straight comes from the exit, one in rotation from the apex, and so on.
/// </summary>
public static class LossBreakdown
{
    /// <summary>The distances that split the piece into <see cref="CornerPhase"/>s, from the best pass.</summary>
    public static int[] Boundaries(CornerPass best)
    {
        var c = best.Corner;
        var start = best.ScoreEndM > best.ScoreStartM ? best.ScoreStartM : c.StartM;
        var end = best.ScoreEndM > best.ScoreStartM ? best.ScoreEndM : c.EndM;
        var brake = best.BrakePointM ?? best.LiftPointM ?? c.StartM;
        var release = best.BrakeReleaseM ?? brake;
        var pickup = best.ThrottlePickupM ?? best.MinSpeedAtM;
        var full = best.FullThrottleM ?? c.EndM;

        var b = new[] { start, brake, release, pickup, full, end };
        for (var i = 1; i < b.Length; i++)
        {
            b[i] = Math.Clamp(b[i], b[i - 1], end);
        }

        return b;
    }

    public static PhaseLoss Split(LapTrace pass, LapTrace best, CornerPass bestPass)
    {
        var b = Boundaries(bestPass);
        var last = Math.Min(pass.Length, best.Length) - 1;
        double Loss(int i)
        {
            var from = Math.Clamp(b[i], 0, last);
            var to = Math.Clamp(b[i + 1], 0, last);
            return (pass.Time[to] - pass.Time[from]) - (best.Time[to] - best.Time[from]);
        }

        return new PhaseLoss(Loss(0), Loss(1), Loss(2), Loss(3), Loss(4));
    }
}

/// <summary>
/// How much grip the car showed it has, learnt from the driver's own laps: the combined (lateral
/// plus longitudinal) acceleration the car reached, per speed band. Grip usage is a reference that
/// doesn't depend on the best pass: a best pass that uses 80% of the grip in a corner still leaves
/// time there.
/// </summary>
public sealed class GripEnvelope
{
    /// <summary>Width of a speed band, m/s.</summary>
    public const float BandWidth = 5;

    /// <summary>The envelope is this percentile of the combined g in each band, so a kerb strike
    /// or a spike doesn't set it.</summary>
    public const double Percentile = 0.95;

    /// <summary>Samples under this combined acceleration aren't cornering or braking, m/s².</summary>
    public const float MinUsefulAccel = 3;

    private readonly double[] _limit;

    private GripEnvelope(double[] limit)
    {
        _limit = limit;
    }

    /// <summary>Learns the envelope from the given laps; null without enough data.</summary>
    public static GripEnvelope? Learn(IEnumerable<LapTrace> laps)
    {
        var bands = new List<List<float>>();
        foreach (var t in laps)
        {
            for (var i = 0; i < t.Length; i++)
            {
                if (t.OffTrack[i] || t.PitRoad[i])
                {
                    continue;
                }

                var g = Combined(t, i);
                if (g < MinUsefulAccel)
                {
                    continue;
                }

                var band = (int)(t.Speed[i] / BandWidth);
                while (bands.Count <= band)
                {
                    bands.Add([]);
                }

                bands[band].Add(g);
            }
        }

        if (bands.Sum(b => b.Count) < 200)
        {
            return null;
        }

        var limit = new double[bands.Count];
        for (var i = 0; i < bands.Count; i++)
        {
            if (bands[i].Count < 20)
            {
                continue;
            }

            bands[i].Sort();
            limit[i] = bands[i][(int)Math.Min(bands[i].Count - 1, Percentile * bands[i].Count)];
        }

        // Bands with too few samples borrow from the nearest band that has a limit.
        for (var i = 0; i < limit.Length; i++)
        {
            if (limit[i] > 0)
            {
                continue;
            }

            for (var d = 1; d < limit.Length; d++)
            {
                if (i - d >= 0 && limit[i - d] > 0)
                {
                    limit[i] = limit[i - d];
                    break;
                }

                if (i + d < limit.Length && limit[i + d] > 0)
                {
                    limit[i] = limit[i + d];
                    break;
                }
            }
        }

        return limit.Any(l => l > 0) ? new GripEnvelope(limit) : null;
    }

    /// <summary>The car's grip limit at a speed, m/s².</summary>
    public double LimitAt(float speed) => _limit[Math.Clamp((int)(speed / BandWidth), 0, _limit.Length - 1)];

    /// <summary>
    /// Average share of the grip limit used from <paramref name="fromM"/> to <paramref name="toM"/>,
    /// 0–1 (can pass 1 briefly); null for a stretch too short to say.
    /// </summary>
    public double? Usage(LapTrace t, int fromM, int toM)
    {
        var from = Math.Clamp(fromM, 0, t.Length - 1);
        var to = Math.Clamp(toM, from, t.Length - 1);
        if (to - from < 5)
        {
            return null;
        }

        double sum = 0;
        for (var i = from; i <= to; i++)
        {
            var limit = LimitAt(t.Speed[i]);
            sum += limit > 0 ? Combined(t, i) / limit : 0;
        }

        return sum / (to - from + 1);
    }

    private static float Combined(LapTrace t, int i) => MathF.Sqrt(t.LatAccel[i] * t.LatAccel[i] + t.LongAccel[i] * t.LongAccel[i]);
}

/// <summary>Consistency and what decides a corner's time, over all its clean passes.</summary>
public static class CornerStats
{
    /// <summary>Passes needed before a correlation means anything.</summary>
    public const int MinPassesForDriver = 6;

    /// <summary>A measurement "decides" the time when it explains it at least this well (|r|).</summary>
    public const double MinCorrelation = 0.5;

    /// <summary>A way that goes against the usual (braking earlier, a lower minimum speed or a later
    /// throttle was quicker) has to explain the time this well before it is said.</summary>
    public const double MinCounterIntuitiveCorrelation = 0.7;

    /// <summary>Sample standard deviation; null for fewer than 3 values.</summary>
    public static double? Spread(IReadOnlyList<double> values)
    {
        if (values.Count < 3)
        {
            return null;
        }

        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    /// <summary>
    /// The measurement that best explains how long the corner took across the clean passes, with
    /// its correlation to the time, and which way helps: "minimum speed" with r = −0.8 means a
    /// higher minimum speed went with a quicker corner. Null when nothing explains it well.
    /// </summary>
    public static (string Measure, double R)? DecidedBy(IReadOnlyList<CornerPass> clean)
    {
        if (clean.Count < MinPassesForDriver)
        {
            return null;
        }

        var times = clean.Select(p => p.ScoreTime).ToArray();
        var candidates = new (string Name, Func<CornerPass, double?> Value)[]
        {
            ("brake point", p => p.BrakePointM),
            ("lift point", p => p.BrakePointM is null ? p.LiftPointM : null),
            ("minimum speed", p => p.MinSpeed),
            ("throttle pick-up", p => p.ThrottlePickupM),
            ("exit speed", p => p.ExitSpeedPlus100),
            ("entry speed", p => p.EntrySpeed),
        };

        (string, double)? best = null;
        foreach (var (name, value) in candidates)
        {
            var pairs = clean.Select((p, i) => (X: value(p), Y: times[i])).Where(x => x.X is not null).Select(x => (X: x.X!.Value, x.Y)).ToList();
            if (pairs.Count < MinPassesForDriver || Correlation(pairs) is not { } r)
            {
                continue;
            }

            var needed = CounterIntuitive(name, r) ? MinCounterIntuitiveCorrelation : MinCorrelation;
            if (Math.Abs(r) >= needed && (best is null || Math.Abs(r) > Math.Abs(best.Value.Item2)))
            {
                best = (name, r);
            }
        }

        return best;
    }

    // r is against time: positive means more of the measurement (later, faster) went with more time.
    private static bool CounterIntuitive(string measure, double r) => measure switch
    {
        "brake point" or "lift point" => r > 0,
        "throttle pick-up" => r < 0,
        _ => r > 0,
    };

    private static double? Correlation(List<(double X, double Y)> pairs)
    {
        var mx = pairs.Average(p => p.X);
        var my = pairs.Average(p => p.Y);
        double sxy = 0, sxx = 0, syy = 0;
        foreach (var (x, y) in pairs)
        {
            sxy += (x - mx) * (y - my);
            sxx += (x - mx) * (x - mx);
            syy += (y - my) * (y - my);
        }

        return sxx > 1e-12 && syy > 1e-12 ? sxy / Math.Sqrt(sxx * syy) : null;
    }
}
