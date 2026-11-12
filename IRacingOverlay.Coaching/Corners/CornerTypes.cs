namespace IRacingOverlay.Coaching.Corners;

/// <summary>
/// What kind of corner it is, which decides what to check in it and how to say it. Taken from the
/// driver's best pass, so the same bend can be a lift corner in a GT3 and a braking corner in a
/// slower car.
/// </summary>
public enum CornerType
{
    /// <summary>Taken flat: any lift is pure loss.</summary>
    Flat,

    /// <summary>A lift (and at most a brake dab): commitment, how little and how short the lift is.</summary>
    Lift,

    /// <summary>Real braking, still fast at the apex: brake point, release and minimum speed.</summary>
    BrakingFast,

    /// <summary>Heavy braking to a low apex speed: the exit, throttle pick-up and traction decide it.</summary>
    Slow,

    /// <summary>Two or more parts (chicane, esses): the exit of the last part matters most.</summary>
    Complex,
}

/// <summary>Sorts a corner into a <see cref="CornerType"/> from one pass through it, normally the best.</summary>
public static class CornerTypes
{
    /// <summary>Peak brake under this, for a short time, is a dab, not braking.</summary>
    public const float DabPeakBrake = 0.20f;

    /// <summary>A dab lasts under this long, seconds.</summary>
    public const double DabSeconds = 0.5;

    /// <summary>Throttle at or above this through the whole corner is flat.</summary>
    public const float FlatThrottle = 0.95f;

    /// <summary>Apex speed under this is a slow corner, m/s (110 km/h). One speed for every corner,
    /// so two corners taken at the same speed always get the same type; it suits GT3 and Cup cars
    /// and will want tuning for slower and faster cars.</summary>
    public const float SlowApexSpeed = 110 / 3.6f;

    public static CornerType Classify(CornerPass p)
    {
        if (p.SpeedDips >= 2)
        {
            return CornerType.Complex;
        }

        var dab = p.BrakePointM is null
            || (p.PeakBrake < DabPeakBrake && p.BrakeLengthM < DabSeconds * Math.Max(p.EntrySpeed, 1));
        if (dab)
        {
            return p.LiftPointM is null || p.MinThrottle >= FlatThrottle ? CornerType.Flat : CornerType.Lift;
        }

        return p.MinSpeed < SlowApexSpeed ? CornerType.Slow : CornerType.BrakingFast;
    }

    /// <summary>
    /// The type of a corner over many passes: the one most of its clean passes show, so it doesn't
    /// flip from lap to lap with whichever pass is the best (a chicane that is sometimes taken as
    /// one slow corner). On a tie, the best pass decides. Null without a clean pass.
    /// </summary>
    public static CornerType? Consensus(IEnumerable<CornerPass> passes, CornerPass? best = null)
    {
        var counts = passes.Where(p => p.IsClean).GroupBy(Classify).Select(g => (Type: g.Key, Count: g.Count())).ToList();
        if (counts.Count == 0)
        {
            return best is null ? null : Classify(best);
        }

        var top = counts.Max(c => c.Count);
        var leaders = counts.Where(c => c.Count == top).Select(c => c.Type).ToList();
        return best is not null && leaders.Contains(Classify(best)) ? Classify(best) : leaders[0];
    }

    /// <summary>How the type reads in a table or debrief.</summary>
    public static string Label(CornerType t) => t switch
    {
        CornerType.Flat => "flat",
        CornerType.Lift => "lift",
        CornerType.BrakingFast => "fast braking",
        CornerType.Slow => "slow",
        CornerType.Complex => "complex",
        _ => t.ToString(),
    };
}
