using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Fuel strategy math for the Fuel Calculator widget. Consumption is measured the same way a race
/// engineer would — the tank level drop across each completed lap — rather than from iRacing's
/// instantaneous FuelUsePerHour, which swings wildly depending on how a given corner was driven.
///
/// The important difference from the simpler <see cref="FuelBuilder"/> is that this one also works
/// in <em>timed</em> races: iRacing reports no lap limit at all for those, so laps-to-go is derived
/// from the session clock and lap time instead. Timed races are the common case in iRacing, and
/// without that fallback every "will I make it" figure is unanswerable for them.
///
/// Instance state, not a static pure function: per-lap consumption inherently has to be remembered
/// across ticks, so one instance is created in MainWindow and reused.
/// </summary>
internal sealed class FuelCalculatorBuilder
{
    // iRacing reports an implausibly large number rather than a null/-1 sentinel when a session has
    // no lap or time limit.
    private const int NoLapLimitThreshold = 20_000;

    // The time sentinel is a 7-day (604800s) clock, so the threshold has to sit below it — and the
    // longest race iRacing runs is 24h, which makes anything past a day and change a sentinel rather
    // than a session clock. Confirmed live: a 1,000,000s threshold let the sentinel through in test
    // drive and turned it into ~5,500 laps to go, i.e. a "fuel to finish" of thousands of liters.
    private const double NoTimeLimitThresholdSeconds = 25 * 60 * 60;

    // Float noise on FuelLevel is far below this; a real refuel is far above it.
    private const double RefuelToleranceLiters = 0.05;

    // Below this fill fraction, deriving capacity from level/percentage amplifies rounding badly.
    private const double MinReliableFuelPct = 0.05;

    private readonly List<double> _lapUsages = [];
    private int? _lapAtLastSample;
    private double _lapStartFuelLevel;
    private bool _lapWindowDirty;
    private bool _wasOnTrack;

    public FuelCalculatorState Build(TelemetrySnapshot telemetry, IracingSessionInfo? session, FuelCalculatorOptions options)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.FuelLevel))
        {
            return FuelCalculatorState.Empty;
        }

        var levelLiters = telemetry.GetFloat(TelemetryVarNames.FuelLevel);
        var levelPct = telemetry.HasVariable(TelemetryVarNames.FuelLevelPct)
            ? telemetry.GetFloat(TelemetryVarNames.FuelLevelPct)
            : 0;

        if (telemetry.HasVariable(TelemetryVarNames.Lap))
        {
            // A missing IsOnTrack defaults to "driving" rather than risk throwing away real laps.
            var isOnTrack = !telemetry.HasVariable(TelemetryVarNames.IsOnTrack)
                || telemetry.GetBool(TelemetryVarNames.IsOnTrack);

            TrackLapConsumption(telemetry.GetInt(TelemetryVarNames.Lap), levelLiters, isOnTrack);
        }

        var average = AverageOver(options.AverageSource.WindowLaps());
        var lapsRemainingWithFuel = average > 0 ? levelLiters / average : 0;
        var lapsLeftInSession = EstimateLapsLeftInSession(telemetry);

        var fuelToFinish = 0.0;
        var fuelDelta = 0.0;
        if (lapsLeftInSession is { } lapsLeft && average > 0)
        {
            // Margin laps are folded in before multiplying so they scale with actual consumption;
            // margin liters are a flat reserve on top.
            fuelToFinish = (lapsLeft + options.MarginLaps) * average + options.MarginLiters;
            fuelDelta = levelLiters - fuelToFinish;
        }

        return new FuelCalculatorState
        {
            LevelLiters = levelLiters,
            LevelPct = levelPct,
            LastLapLiters = _lapUsages.Count > 0 ? _lapUsages[^1] : 0,
            AverageLiters = average,
            MinLiters = _lapUsages.Count > 0 ? _lapUsages.Min() : 0,
            MaxLiters = _lapUsages.Count > 0 ? _lapUsages.Max() : 0,
            LapsRemainingWithFuel = lapsRemainingWithFuel,
            LapsLeftInSession = lapsLeftInSession,
            FuelToFinishLiters = fuelToFinish,
            FuelDeltaLiters = fuelDelta,
            TankCapacityLiters = ResolveTankCapacity(session, levelLiters, levelPct),
        };
    }

    /// <summary>
    /// Usable capacity: the car's physical tank scaled by any series fuel restriction. Falls back to
    /// deriving it from the level and its own percentage (FuelLevelPct is a fraction of maximum) for
    /// the window before session info has been parsed, or if a car doesn't report the YAML fields.
    /// A near-empty tank makes that division wildly imprecise, so it's only trusted above a floor.
    /// </summary>
    private static double ResolveTankCapacity(IracingSessionInfo? session, double levelLiters, double levelPct)
    {
        if (session?.DriverInfo is { DriverCarFuelMaxLtr: > 0 } driverInfo)
        {
            var allowedFraction = driverInfo.DriverCarMaxFuelPct is > 0 and <= 1 ? driverInfo.DriverCarMaxFuelPct : 1;
            return driverInfo.DriverCarFuelMaxLtr * allowedFraction;
        }

        return levelPct > MinReliableFuelPct ? levelLiters / levelPct : 0;
    }

    private double AverageOver(int windowLaps)
    {
        if (_lapUsages.Count == 0)
        {
            return 0;
        }

        var take = Math.Min(windowLaps, _lapUsages.Count);
        var total = 0.0;
        for (var i = _lapUsages.Count - take; i < _lapUsages.Count; i++)
        {
            total += _lapUsages[i];
        }

        return total / take;
    }

    /// <summary>
    /// Records the tank level at each lap boundary and files the difference as that lap's usage.
    /// A measuring window is thrown away rather than recorded whenever the tank was refilled during
    /// it, since the fuel burned across a lap containing a pit stop simply can't be read off the
    /// level difference.
    /// </summary>
    private void TrackLapConsumption(int currentLap, double levelLiters, bool isOnTrack)
    {
        if (_lapAtLastSample is null || currentLap < _lapAtLastSample)
        {
            // First sample, or the lap counter went backwards (new session) — restart from here
            // instead of carrying stale data from the previous session into the average. Seeding
            // _wasOnTrack from the current state matters: treating the very first sample as a
            // garage->car transition would swallow the lap that follows it.
            _lapUsages.Clear();
            _wasOnTrack = isOnTrack;
            StartLapWindow(currentLap, levelLiters, dirty: false);
            return;
        }

        if (!isOnTrack)
        {
            // Garage/setup screen, spectating or a replay: no lap is being run, and the fuel load
            // can be changed freely here, so keep rebasing instead of measuring.
            _wasOnTrack = false;
            StartLapWindow(currentLap, levelLiters, dirty: false);
            return;
        }

        var justEnteredCar = !_wasOnTrack;
        _wasOnTrack = true;

        var lapsElapsed = currentLap - _lapAtLastSample.Value;
        if (lapsElapsed <= 0)
        {
            if (justEnteredCar)
            {
                // Whatever the tank holds now is the true starting level however it got there. This
                // is the transition that used to silently kill the out-lap: the baseline had been
                // taken back on the garage screen before race fuel was loaded, so completing the
                // out-lap looked like the level had gone UP and the lap was discarded as a refuel.
                StartLapWindow(currentLap, levelLiters, dirty: false);
            }
            else if (levelLiters > _lapStartFuelLevel + RefuelToleranceLiters)
            {
                // Refuelled mid-lap. Measuring from the new level would charge this lap only for
                // the distance run after the stop, under-reporting it and dragging the average
                // down, so the whole window is marked unusable and a clean one starts next lap.
                StartLapWindow(currentLap, levelLiters, dirty: true);
            }

            return;
        }

        var used = _lapStartFuelLevel - levelLiters;
        if (!_lapWindowDirty && !justEnteredCar && used > 0)
        {
            // Spread over however many laps actually elapsed, so a missed tick that lets the lap
            // counter jump by more than one doesn't record a single double-sized lap.
            var perLap = used / lapsElapsed;
            for (var i = 0; i < lapsElapsed; i++)
            {
                _lapUsages.Add(perLap);
            }
        }

        StartLapWindow(currentLap, levelLiters, dirty: false);
    }

    private void StartLapWindow(int lap, double levelLiters, bool dirty)
    {
        _lapAtLastSample = lap;
        _lapStartFuelLevel = levelLiters;
        _lapWindowDirty = dirty;
    }

    /// <summary>
    /// Laps left to run. Prefers iRacing's own lap counter; for a timed session (where that counter
    /// reports "no limit") it falls back to the session clock divided by lap time. Rounded up
    /// because a timed race ends when the leader completes the lap in progress as the clock hits
    /// zero — the part-lap still has to be fuelled.
    /// </summary>
    private static double? EstimateLapsLeftInSession(TelemetrySnapshot telemetry)
    {
        if (telemetry.HasVariable(TelemetryVarNames.SessionLapsRemain))
        {
            var raw = telemetry.GetInt(TelemetryVarNames.SessionLapsRemain);
            if (raw is > 0 and < NoLapLimitThreshold)
            {
                return raw;
            }
        }

        if (telemetry.HasVariable(TelemetryVarNames.SessionTimeRemain))
        {
            var secondsRemaining = telemetry.GetDouble(TelemetryVarNames.SessionTimeRemain);
            var lapTime = ReferenceLapTime(telemetry);
            if (secondsRemaining > 0 && secondsRemaining < NoTimeLimitThresholdSeconds && lapTime > 0)
            {
                return Math.Ceiling(secondsRemaining / lapTime);
            }
        }

        return null;
    }

    /// <summary>Last lap first (it reflects current fuel load, tires, and traffic), falling back to
    /// the session best before any lap is complete.</summary>
    private static double ReferenceLapTime(TelemetrySnapshot telemetry)
    {
        if (telemetry.HasVariable(TelemetryVarNames.PlayerLastLapTime))
        {
            var last = telemetry.GetFloat(TelemetryVarNames.PlayerLastLapTime);
            if (last > 0)
            {
                return last;
            }
        }

        if (telemetry.HasVariable(TelemetryVarNames.PlayerBestLapTime))
        {
            var best = telemetry.GetFloat(TelemetryVarNames.PlayerBestLapTime);
            if (best > 0)
            {
                return best;
            }
        }

        return 0;
    }
}
