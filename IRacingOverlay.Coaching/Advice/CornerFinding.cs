namespace IRacingOverlay.Coaching.Advice;

/// <summary>What was different, in one corner, from the driver's best pass through it.</summary>
public enum FindingKind
{
    /// <summary>Braked later than the best pass and paid for it (slower at the apex or later on the throttle).</summary>
    BrakedTooLate,

    /// <summary>Braked earlier than the best pass.</summary>
    BrakedTooEarly,

    /// <summary>A softer or slower first hit of the brake than the best pass.</summary>
    InitialBrakeTooSoft,

    /// <summary>More brake pressure than the best pass, and slower through the corner for it.</summary>
    InitialBrakeTooHard,

    /// <summary>Released the brake sooner after turn-in than the best pass, and slower at the apex.</summary>
    TooLittleTrailBrake,

    /// <summary>Back on the throttle later than the best pass.</summary>
    LateThrottle,

    /// <summary>Slower at the apex than the best pass.</summary>
    LowMinimumSpeed,

    /// <summary>Slower 100 m after the corner than the best pass.</summary>
    SlowExit,
}

/// <summary>
/// One difference from the best pass. <see cref="Amount"/> is always positive and in the unit that
/// fits the kind: metres for brake point, trail brake and throttle; km/h for speeds; percentage
/// points of pedal for brake pressure (or metres of ramp when <see cref="IsRamp"/>).
/// </summary>
public sealed record CornerFinding(FindingKind Kind, double Amount)
{
    /// <summary>For <see cref="FindingKind.InitialBrakeTooSoft"/>: the pressure was right but it took
    /// longer to build, so <see cref="Amount"/> is the extra metres of ramp.</summary>
    public bool IsRamp { get; init; }
}
