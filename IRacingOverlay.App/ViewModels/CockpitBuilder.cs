using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Builds the gear/shift-light/ABS/proximity display state for CockpitWidget. There's no TC bar:
/// iRacing exposes no traction-control-intervention telemetry and no wheel-speed data to derive one
/// from (confirmed — a deliberate anti-cheat limitation, the same wall the SimHub community has hit),
/// so rather than ship an approximation with no ground truth to verify against, that indicator was
/// dropped entirely.
/// </summary>
internal static class CockpitBuilder
{
    // Rough width of a race car for turning a longitudinal (along-track) gap into an "how alongside
    // are we" fraction. iRacing's telemetry doesn't expose other cars' lateral position at all, so
    // this — combined with CarLeftRight for which side — is an approximation, not a measurement.
    private const double CarLengthMeters = 4.8;

    public static CockpitState Build(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        var gear = BuildGearText(telemetry);
        var (litCount, blink) = BuildShiftLights(telemetry, session);
        var abs = telemetry.HasVariable(TelemetryVarNames.BrakeAbsActive) && telemetry.GetBool(TelemetryVarNames.BrakeAbsActive);
        var (left, right) = BuildProximity(telemetry, session);
        var speedKph = telemetry.HasVariable(TelemetryVarNames.Speed) ? telemetry.GetFloat(TelemetryVarNames.Speed) * 3.6 : 0;
        var rpm = telemetry.HasVariable(TelemetryVarNames.Rpm) ? telemetry.GetFloat(TelemetryVarNames.Rpm) : 0;

        return new CockpitState
        {
            Gear = gear,
            ShiftLightsLit = litCount,
            ShiftBlink = blink,
            AbsActive = abs,
            SpeedKph = speedKph,
            Rpm = rpm,
            LeftProximity = left,
            RightProximity = right,
        };
    }

    private static string BuildGearText(TelemetrySnapshot telemetry)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.Gear))
        {
            return "–";
        }

        var gear = telemetry.GetInt(TelemetryVarNames.Gear);
        return gear switch
        {
            -1 => "R",
            0 => "N",
            _ => gear.ToString(),
        };
    }

    private static (int litCount, bool blink) BuildShiftLights(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.Rpm) || session?.DriverInfo is not { } driverInfo)
        {
            return (0, false);
        }

        var rpm = telemetry.GetFloat(TelemetryVarNames.Rpm);
        var first = driverInfo.DriverCarSLFirstRPM;
        var shift = driverInfo.DriverCarSLShiftRPM;
        var blinkRpm = driverInfo.DriverCarSLBlinkRPM;

        if (shift <= first)
        {
            return (0, false); // car didn't report usable shift-light thresholds
        }

        var fraction = Math.Clamp((rpm - first) / (shift - first), 0, 1);
        var litCount = (int)Math.Round(fraction * CockpitState.ShiftLightCount);
        var blink = blinkRpm > 0 && rpm >= blinkRpm;
        return (litCount, blink);
    }

    private static (ProximitySide left, ProximitySide right) BuildProximity(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (session?.DriverInfo is not { } driverInfo ||
            !telemetry.HasVariable(TelemetryVarNames.CarLeftRight) ||
            !telemetry.HasVariable(TelemetryVarNames.CarIdxEstTime) ||
            !telemetry.HasVariable(TelemetryVarNames.Speed))
        {
            return (ProximitySide.None, ProximitySide.None);
        }

        // Docs describe this as a bitfield, but the live shared-memory var header actually reports
        // it as a plain Int (confirmed against a running session) — read it as such.
        var carLeftRight = telemetry.GetInt(TelemetryVarNames.CarLeftRight);

        var playerCarIdx = driverInfo.DriverCarIdx;
        var carIdxEstTime = telemetry.GetFloatArray(TelemetryVarNames.CarIdxEstTime);
        var speed = telemetry.GetFloat(TelemetryVarNames.Speed);

        if (playerCarIdx < 0 || playerCarIdx >= carIdxEstTime.Length || speed <= 0.5)
        {
            return (ProximitySide.None, ProximitySide.None);
        }

        // Track the closest car by absolute gap, but keep its SIGN too — a positive gap means the
        // other car's front is ahead of ours (we're catching up from behind or just clearing them
        // after a pass); negative means their front is behind ours (we've drawn ahead, or they're
        // about to draw level from behind). That sign is what lets ComputeBand place the overlap at
        // the right end of our own car instead of just reporting how much of them is alongside.
        // Deliberately class-agnostic — same technique the Relative/Standings/Track Map widgets use,
        // all confirmed live to work correctly across classes.
        var minAbsGapMeters = double.MaxValue;
        var closestSignedGapMeters = 0.0;
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx == playerCarIdx || driver.CarIdx < 0 || driver.CarIdx >= carIdxEstTime.Length)
            {
                continue;
            }

            // Nearby-car gaps are dominated by the same-lap term, so plain CarIdxEstTime difference
            // (no lap-count correction) is accurate enough for "is this car within a car length of me."
            // Positive => the other car's EstTime (progress along track) is further than ours, i.e.
            // their front is ahead of ours.
            var gapSeconds = carIdxEstTime[driver.CarIdx] - carIdxEstTime[playerCarIdx];
            var gapMeters = gapSeconds * speed;
            var absGapMeters = Math.Abs(gapMeters);
            if (absGapMeters < minAbsGapMeters)
            {
                minAbsGapMeters = absGapMeters;
                closestSignedGapMeters = gapMeters;
            }
        }

        if (minAbsGapMeters == double.MaxValue)
        {
            return (ProximitySide.None, ProximitySide.None);
        }

        var side = ComputeBand(closestSignedGapMeters);
        if (side.Amount <= 0)
        {
            return (ProximitySide.None, ProximitySide.None); // closest car is further than a car length away
        }

        // irsdk_CarLeftRight: 0=Off, 1=Clear, 2=CarLeft, 3=CarRight, 4=CarLeftRight, 5=2CarsLeft, 6=2CarsRight.
        return carLeftRight switch
        {
            2 or 5 => (side, ProximitySide.None),
            3 or 6 => (ProximitySide.None, side),
            4 => (side, side),
            _ => (ProximitySide.None, ProximitySide.None),
        };
    }

    /// <summary>
    /// Projects the other car's signed along-track offset onto our own car's front-to-rear span,
    /// returning the band of OUR car (0 = front, 1 = rear) that they currently overlap.
    /// </summary>
    private static ProximitySide ComputeBand(double signedGapMeters)
    {
        double bandStart, bandEnd;
        if (signedGapMeters >= 0)
        {
            // Their front is at or ahead of ours: overlap runs from our front down to wherever
            // their front currently is — shrinks toward the top as they pull clear ahead.
            bandStart = 0;
            bandEnd = Math.Clamp(CarLengthMeters - signedGapMeters, 0, CarLengthMeters);
        }
        else
        {
            // Their front is behind ours: overlap runs from wherever their front is up to our
            // rear — shrinks toward the bottom as they fall clear behind.
            bandStart = Math.Clamp(-signedGapMeters, 0, CarLengthMeters);
            bandEnd = CarLengthMeters;
        }

        var amount = Math.Clamp((bandEnd - bandStart) / CarLengthMeters, 0, 1);
        return new ProximitySide(amount, bandStart / CarLengthMeters, bandEnd / CarLengthMeters);
    }
}
