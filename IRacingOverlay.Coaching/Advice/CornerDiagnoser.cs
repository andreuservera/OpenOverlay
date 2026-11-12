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
        var findings = new List<CornerFinding>();
        if (ReferenceEquals(pass, best))
        {
            return findings;
        }

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

        if (apexSlower)
        {
            findings.Add(new CornerFinding(FindingKind.LowMinimumSpeed, apexSlowerKmh));
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
