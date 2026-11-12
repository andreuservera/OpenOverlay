using IRacingOverlay.Coaching.Corners;

namespace IRacingOverlay.Coaching.Advice;

/// <summary>How big a difference from the best pass has to be before it is worth saying.</summary>
public sealed record DiagnosisOptions
{
    /// <summary>Brake point moved by at least this, metres.</summary>
    public int BrakePointMetres { get; init; } = 8;

    /// <summary>Peak brake pressure differs by at least this share of the pedal (0.08 = 8 points).</summary>
    public float PeakBrakeShare { get; init; } = 0.08f;

    /// <summary>The pressure took at least this many metres longer to build.</summary>
    public int BrakeRampMetres { get; init; } = 8;

    /// <summary>Brake carried this many metres less past turn-in.</summary>
    public int TrailBrakeMetres { get; init; } = 10;

    /// <summary>Back on the throttle at least this many metres later.</summary>
    public int ThrottleMetres { get; init; } = 10;

    /// <summary>Apex or exit speed lower by at least this, km/h.</summary>
    public double SpeedKmh { get; init; } = 2.0;

    /// <summary>Lifted deeper by at least this share of the pedal.</summary>
    public float LiftDepthShare { get; init; } = 0.15f;

    /// <summary>Coasted at least this many metres longer (and at least this long in all).</summary>
    public int CoastingMetres { get; init; } = 10;

    /// <summary>Steering per g up by at least this share is understeer.</summary>
    public double UndersteerShare { get; init; } = 0.15;
}

/// <summary>
/// Compares one pass through a corner with the driver's best pass through it and says what was
/// different, causes first: braking, then the apex, then the exit, so the first finding is the one to fix. Only differences a driver can act
/// on and big enough to matter are reported, so a lap that matched the best gives none.
/// </summary>
public static class CornerDiagnoser
{
    private const double MsToKmh = 3.6;

    public static IReadOnlyList<CornerFinding> Diagnose(CornerPass pass, CornerPass best, DiagnosisOptions? options = null)
    {
        var o = options ?? new DiagnosisOptions();
        if (ReferenceEquals(pass, best))
        {
            return [];
        }

        return CornerTypes.Classify(best) switch
        {
            CornerType.Flat or CornerType.Lift => DiagnoseLift(pass, best, o),
            CornerType.Complex => ExitFirst(DiagnoseBraking(pass, best, o, slow: true)),
            CornerType.Slow => DiagnoseBraking(pass, best, o, slow: true),
            _ => DiagnoseBraking(pass, best, o, slow: false),
        };
    }

    /// <summary>
    /// Flat and lift corners are about commitment: braking where a lift was enough, lifting where
    /// flat was possible, lifting too early, too deep or for too long.
    /// </summary>
    private static List<CornerFinding> DiagnoseLift(CornerPass pass, CornerPass best, DiagnosisOptions o)
    {
        var findings = new List<CornerFinding>();
        var bestType = CornerTypes.Classify(best);
        var passBraked = pass.BrakePointM is not null && pass.PeakBrake >= CornerTypes.DabPeakBrake;
        if (passBraked && (best.BrakePointM is null || best.PeakBrake < CornerTypes.DabPeakBrake))
        {
            findings.Add(new CornerFinding(FindingKind.UnneededBrake, pass.PeakBrake * 100));
        }

        if (bestType == CornerType.Flat)
        {
            if (pass.LiftPointM is not null && pass.MinThrottle < CornerTypes.FlatThrottle - 0.05f)
            {
                findings.Add(new CornerFinding(FindingKind.LiftedInFlatCorner, (1 - pass.MinThrottle) * 100));
            }
        }
        else if (pass.LiftPointM is { } lp && best.LiftPointM is { } blp)
        {
            if (blp - lp >= o.BrakePointMetres)
            {
                findings.Add(new CornerFinding(FindingKind.LiftedTooEarly, blp - lp));
            }

            if (best.MinThrottle - pass.MinThrottle >= o.LiftDepthShare && !passBraked)
            {
                findings.Add(new CornerFinding(FindingKind.LiftTooDeep, (best.MinThrottle - pass.MinThrottle) * 100));
            }

            if (pass.LiftLengthM - best.LiftLengthM >= o.ThrottleMetres)
            {
                findings.Add(new CornerFinding(FindingKind.LiftTooLong, pass.LiftLengthM - best.LiftLengthM));
            }
        }

        var apexSlowerKmh = (best.MinSpeed - pass.MinSpeed) * MsToKmh;
        if (apexSlowerKmh >= o.SpeedKmh)
        {
            findings.Add(new CornerFinding(FindingKind.LowMinimumSpeed, apexSlowerKmh));
        }

        var exitSlowerKmh = (best.ExitSpeedPlus100 - pass.ExitSpeedPlus100) * MsToKmh;
        if (exitSlowerKmh >= o.SpeedKmh)
        {
            findings.Add(new CornerFinding(FindingKind.SlowExit, exitSlowerKmh));
        }

        return findings;
    }

    /// <summary>In a chicane the first part is sacrificed for the exit of the last one, so the
    /// exit comes first.</summary>
    private static List<CornerFinding> ExitFirst(List<CornerFinding> findings) =>
        findings.OrderBy(f => f.Kind is FindingKind.SlowExit or FindingKind.LateThrottle or FindingKind.ThrottleHesitation ? 0 : 1).ToList();

    /// <summary>Braking corners: brake point and pressure, rotation, then the exit.</summary>
    private static List<CornerFinding> DiagnoseBraking(CornerPass pass, CornerPass best, DiagnosisOptions o, bool slow)
    {
        var findings = new List<CornerFinding>();

        var apexSlowerKmh = (best.MinSpeed - pass.MinSpeed) * MsToKmh;
        var exitSlowerKmh = (best.ExitSpeedPlus100 - pass.ExitSpeedPlus100) * MsToKmh;
        var apexSlower = apexSlowerKmh >= o.SpeedKmh;
        int? throttleLaterM = pass.ThrottlePickupM is { } tp && best.ThrottlePickupM is { } btp ? tp - btp : null;

        // Braking point.
        if (pass.BrakePointM is { } bp && best.BrakePointM is { } bbp)
        {
            var later = bp - bbp;
            if (later <= -o.BrakePointMetres)
            {
                findings.Add(new CornerFinding(FindingKind.BrakedTooEarly, -later));
            }
            else if (later >= o.BrakePointMetres && (apexSlower || throttleLaterM >= o.ThrottleMetres))
            {
                // Later on the brakes only helps if the corner still works; here it didn't.
                findings.Add(new CornerFinding(FindingKind.BrakedTooLate, later));
            }
        }

        // Initial brake: how hard, and how quickly it got there.
        if (pass.BrakePointM is not null && best.BrakePointM is not null)
        {
            var softer = best.PeakBrake - pass.PeakBrake;
            var slowerRamp = (pass.BrakeRampM ?? 0) - (best.BrakeRampM ?? 0);
            if (softer >= o.PeakBrakeShare)
            {
                findings.Add(new CornerFinding(FindingKind.InitialBrakeTooSoft, softer * 100));
            }
            else if (slowerRamp >= o.BrakeRampMetres)
            {
                findings.Add(new CornerFinding(FindingKind.InitialBrakeTooSoft, slowerRamp) { IsRamp = true });
            }
            else if (-softer >= o.PeakBrakeShare && apexSlower)
            {
                findings.Add(new CornerFinding(FindingKind.InitialBrakeTooHard, -softer * 100));
            }
        }

        // Trail braking: the brake helps turn the car in; letting go early leaves speed on the table.
        if (best.TrailBrakeM - pass.TrailBrakeM >= o.TrailBrakeMetres && apexSlower)
        {
            findings.Add(new CornerFinding(FindingKind.TooLittleTrailBrake, best.TrailBrakeM - pass.TrailBrakeM));
        }

        // Rotation: coasting between the pedals, and a car that pushed instead of turning.
        if (pass.CoastingM - best.CoastingM >= o.CoastingMetres && pass.CoastingM >= 15)
        {
            findings.Add(new CornerFinding(FindingKind.Coasting, pass.CoastingM));
        }

        if (apexSlower && pass.SteerPerLatAccel is { } sg && best.SteerPerLatAccel is { } bsg && bsg > 0 && sg / bsg - 1 >= o.UndersteerShare)
        {
            findings.Add(new CornerFinding(FindingKind.Understeer, (sg / bsg - 1) * 100));
        }

        if (apexSlower)
        {
            findings.Add(new CornerFinding(FindingKind.LowMinimumSpeed, apexSlowerKmh));
        }

        // Traction matters most where the car accelerates from low speed.
        if (slow && pass.ThrottleLifts > best.ThrottleLifts)
        {
            findings.Add(new CornerFinding(FindingKind.ThrottleHesitation, pass.ThrottleLifts - best.ThrottleLifts));
        }

        if (throttleLaterM >= o.ThrottleMetres)
        {
            findings.Add(new CornerFinding(FindingKind.LateThrottle, throttleLaterM!.Value));
        }

        if (exitSlowerKmh >= o.SpeedKmh)
        {
            findings.Add(new CornerFinding(FindingKind.SlowExit, exitSlowerKmh));
        }

        return findings;
    }
}
