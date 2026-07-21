using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Builds the gear/shift-light/ABS/TC/proximity display state for CockpitWidget.
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
        var tc = telemetry.HasVariable(TelemetryVarNames.TractionControlToggle) && telemetry.GetBool(TelemetryVarNames.TractionControlToggle);
        var (left, right) = BuildProximity(telemetry, session);

        return new CockpitState
        {
            Gear = gear,
            ShiftLightsLit = litCount,
            ShiftBlink = blink,
            AbsActive = abs,
            TcActive = tc,
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

    private static (double left, double right) BuildProximity(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (session?.DriverInfo is not { } driverInfo ||
            !telemetry.HasVariable(TelemetryVarNames.CarLeftRight) ||
            !telemetry.HasVariable(TelemetryVarNames.CarIdxEstTime) ||
            !telemetry.HasVariable(TelemetryVarNames.Speed))
        {
            return (0, 0);
        }

        // Docs describe this as a bitfield, but the live shared-memory var header actually reports
        // it as a plain Int (confirmed against a running session) — read it as such.
        var carLeftRight = telemetry.GetInt(TelemetryVarNames.CarLeftRight);
        if (carLeftRight <= 1) // irsdk_LROff or irsdk_LRClear — nobody alongside
        {
            return (0, 0);
        }

        var playerCarIdx = driverInfo.DriverCarIdx;
        var carIdxEstTime = telemetry.GetFloatArray(TelemetryVarNames.CarIdxEstTime);
        var speed = telemetry.GetFloat(TelemetryVarNames.Speed);

        if (playerCarIdx < 0 || playerCarIdx >= carIdxEstTime.Length || speed <= 0.5)
        {
            return (0, 0);
        }

        var minGapMeters = double.MaxValue;
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx == playerCarIdx || driver.CarIdx < 0 || driver.CarIdx >= carIdxEstTime.Length)
            {
                continue;
            }

            // Nearby-car gaps are dominated by the same-lap term, so plain CarIdxEstTime difference
            // (no lap-count correction) is accurate enough for "is this car within a car length of me."
            var gapSeconds = carIdxEstTime[playerCarIdx] - carIdxEstTime[driver.CarIdx];
            var gapMeters = Math.Abs(gapSeconds) * speed;
            minGapMeters = Math.Min(minGapMeters, gapMeters);
        }

        if (minGapMeters == double.MaxValue)
        {
            return (0, 0);
        }

        var overlap = Math.Clamp(1 - minGapMeters / CarLengthMeters, 0, 1);

        // irsdk_CarLeftRight: 2=CarLeft, 3=CarRight, 4=CarLeftRight, 5=2CarsLeft, 6=2CarsRight
        return carLeftRight switch
        {
            2 or 5 => (overlap, 0),
            3 or 6 => (0, overlap),
            4 => (overlap, overlap),
            _ => (0, 0),
        };
    }
}
