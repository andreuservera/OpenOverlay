using System.Globalization;
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
            AbsLevel = Optional(telemetry, TelemetryVarNames.AbsSetting) is { } level ? (int)Math.Round(level) : null,
            SpeedKph = speedKph,
            Rpm = rpm,
            LeftProximity = left,
            RightProximity = right,
            FuelLiters = Optional(telemetry, TelemetryVarNames.FuelLevel),
            FuelPct = Optional(telemetry, TelemetryVarNames.FuelLevelPct),
            Throttle = Optional(telemetry, TelemetryVarNames.Throttle) ?? 0,
            Brake = Optional(telemetry, TelemetryVarNames.Brake) ?? 0,
            WaterTempC = Optional(telemetry, TelemetryVarNames.WaterTemp),
            OilTempC = Optional(telemetry, TelemetryVarNames.OilTemp),
            UnitSystem = Units.Read(telemetry),
        };
    }

    private static double? Optional(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? telemetry.GetFloat(name) : null;

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

        if (shift <= first)
        {
            return (0, false); // car didn't report usable shift-light thresholds
        }

        var fraction = Math.Clamp((rpm - first) / (shift - first), 0, 1);
        var litCount = (int)Math.Round(fraction * CockpitState.ShiftLightCount);

        // Flash from the shift point, when every light is lit. iRacing's own blink RPM usually sits
        // up at the limiter, which a driver shifting on the lights never reaches.
        return (litCount, rpm >= shift);
    }

    private static (ProximitySide left, ProximitySide right) BuildProximity(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (session?.DriverInfo is not { } driverInfo ||
            ParseTrackLengthMeters(session.WeekendInfo?.TrackLength) is not { } trackLengthMeters ||
            !telemetry.HasVariable(TelemetryVarNames.CarLeftRight) ||
            !telemetry.HasVariable(TelemetryVarNames.CarIdxLapDistPct) ||
            !telemetry.HasVariable(TelemetryVarNames.Speed))
        {
            return (ProximitySide.None, ProximitySide.None);
        }

        // Docs describe this as a bitfield, but the live shared-memory var header actually reports
        // it as a plain Int (confirmed against a running session) — read it as such.
        var carLeftRight = telemetry.GetInt(TelemetryVarNames.CarLeftRight);

        var playerCarIdx = driverInfo.DriverCarIdx;
        var lapDistPct = telemetry.GetFloatArray(TelemetryVarNames.CarIdxLapDistPct);
        var speed = telemetry.GetFloat(TelemetryVarNames.Speed);

        if (playerCarIdx < 0 || playerCarIdx >= lapDistPct.Length || lapDistPct[playerCarIdx] < 0 || speed <= 0.5)
        {
            return (ProximitySide.None, ProximitySide.None);
        }

        // Track the closest car by absolute gap, but keep its SIGN too — a positive gap means the
        // other car's front is ahead of ours (we're catching up from behind or just clearing them
        // after a pass); negative means their front is behind ours (we've drawn ahead, or they're
        // about to draw level from behind). That sign is what lets ComputeBand place the overlap at
        // the right end of our own car instead of just reporting how much of them is alongside.
        // Track position (TrackPosition's on-track fold), not CarIdxEstTime: EstTime is scaled by each
        // car's own CarClassEstLapTime, so it can't be compared between classes (or BoP'd models) —
        // that kept multiclass bars dark.
        var minAbsGapMeters = double.MaxValue;
        var closestSignedGapMeters = 0.0;
        foreach (var driver in driverInfo.Drivers)
        {
            if (driver.IsPaceCar || driver.CarIdx == playerCarIdx || driver.CarIdx < 0 || driver.CarIdx >= lapDistPct.Length ||
                lapDistPct[driver.CarIdx] < 0) // -1 = not in world; would otherwise fold to "alongside" near S/F
            {
                continue;
            }

            // Wrapped to ±half a lap across S/F. Lap count is ignored on purpose: lapped traffic alongside is still alongside.
            var gapPct = TrackPosition.FoldToHalfLap((double)lapDistPct[driver.CarIdx] - lapDistPct[playerCarIdx]);
            var gapMeters = gapPct * trackLengthMeters;
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

    /// <summary>"5.7536 km" → 5753.6. iRacing writes the session's TrackLength in kilometres; anything unparsable is null.</summary>
    private static double? ParseTrackLengthMeters(string? text)
    {
        var km = text?.Replace("km", "").Trim();
        return double.TryParse(km, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value * 1000
            : null;
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
