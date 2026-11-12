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

    /// <summary>Apex speed under this is a slow corner, m/s (110 km/h)…</summary>
    public const float SlowApexSpeed = 110 / 3.6f;

    /// <summary>…as is losing more than this share of the entry speed.</summary>
    public const float SlowSpeedShare = 0.55f;

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

        return p.MinSpeed < SlowApexSpeed || p.MinSpeed < SlowSpeedShare * p.EntrySpeed
            ? CornerType.Slow
            : CornerType.BrakingFast;
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
