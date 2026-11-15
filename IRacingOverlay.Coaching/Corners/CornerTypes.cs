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

    public static CornerType Classify(CornerPass p) => Classify(p, SlowApexSpeed);

    /// <summary>As <see cref="Classify(CornerPass)"/>, with the car's own idea of a slow corner.</summary>
    public static CornerType Classify(CornerPass p, CarProfile? profile) => Classify(p, profile?.SlowApexSpeed ?? SlowApexSpeed);

    private static CornerType Classify(CornerPass p, float slowApexSpeed)
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

        return p.MinSpeed < slowApexSpeed ? CornerType.Slow : CornerType.BrakingFast;
    }

    /// <summary>A corner's type is fixed from its first this-many clean passes.</summary>
    public const int SettledAfterPasses = 5;

    /// <summary>A type is only saved for later sessions (<see cref="CornerTypeStore"/>) when at least
    /// this share of the settling passes show it (4 of 5); a corner whose passes are split between
    /// two types keeps being agreed per session. A saved type is replaced when this share of a later
    /// session's clean passes (at least <see cref="SettledAfterPasses"/>) show another type.</summary>
    public const double SavedShare = 0.8;

    /// <summary>
    /// The type of a corner over many passes: the one most of its clean passes show, so it doesn't
    /// flip from lap to lap with whichever pass is the best (a chicane that is sometimes taken as
    /// one slow corner). On a tie, the best pass decides. From <see cref="SettledAfterPasses"/>
    /// clean passes on, only the first ones count, so the type no longer changes. Null without a clean pass.
    /// </summary>
    public static CornerType? Consensus(IEnumerable<CornerPass> passes, CornerPass? best = null, CarProfile? profile = null)
    {
        CornerType Classify(CornerPass p) => CornerTypes.Classify(p, profile);
        // Settled once enough passes agree it: later passes don't move it, so the debriefs of one
        // session never call the same corner by two types.
        var clean = passes.Where(p => p.IsClean).OrderBy(p => p.LapNumber).ToList();
        if (clean.Count >= SettledAfterPasses)
        {
            clean = clean.Take(SettledAfterPasses).ToList();
            best = null;
        }

        var counts = clean.GroupBy(Classify).Select(g => (Type: g.Key, Count: g.Count())).ToList();
        if (counts.Count == 0)
        {
            return best is null ? null : Classify(best);
        }

        var top = counts.Max(c => c.Count);
        var leaders = counts.Where(c => c.Count == top).Select(c => c.Type).ToList();
        return best is not null && leaders.Contains(Classify(best)) ? Classify(best) : leaders[0];
    }

    /// <summary>The type at least <see cref="SavedShare"/> of <paramref name="passes"/> show, or
    /// null when they are split (or there are none).</summary>
    public static CornerType? Decisive(IReadOnlyCollection<CornerPass> passes, CarProfile? profile = null)
    {
        CornerType Classify(CornerPass p) => CornerTypes.Classify(p, profile);
        if (passes.Count == 0)
        {
            return null;
        }

        var top = passes.GroupBy(Classify).MaxBy(g => g.Count())!;
        return (double)top.Count() / passes.Count >= SavedShare ? top.Key : null;
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
