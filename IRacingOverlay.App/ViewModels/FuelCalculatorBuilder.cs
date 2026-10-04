using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Fuel strategy math for the Fuel Calculator widget. Consumption is measured the same way a race
/// engineer would — the tank level drop across each completed lap — rather than from iRacing's
/// instantaneous FuelUsePerHour, which swings wildly depending on how a given corner was driven.
///
/// It also works in <em>timed</em> races: iRacing reports no lap limit at all for those, so laps-to-go is derived
/// from the session clock and lap time instead. Timed races are the common case in iRacing, and
/// without that fallback every "will I make it" figure is unanswerable for them.
///
/// Which laps count is <see cref="LapLog"/>'s call: out laps, pit laps and the formation lap never
/// reach the averages, the projections or the remaining-fuel figures.
/// </summary>
internal sealed class FuelCalculatorBuilder
{
    // Below this fill fraction, deriving capacity from level/percentage amplifies rounding badly.
    private const double MinReliableFuelPct = 0.05;

    private readonly LapLog _laps;
    private readonly bool _ownsLog;

    /// <summary>Keeps its own lap log, fed by <see cref="Build"/>.</summary>
    public FuelCalculatorBuilder()
        : this(new LapLog(), ownsLog: true)
    {
    }

    /// <summary>Reads a lap log someone else keeps up to date, so the history survives the widget
    /// being closed.</summary>
    public FuelCalculatorBuilder(LapLog laps)
        : this(laps, ownsLog: false)
    {
    }

    private FuelCalculatorBuilder(LapLog laps, bool ownsLog)
    {
        _laps = laps;
        _ownsLog = ownsLog;
    }

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

        if (_ownsLog)
        {
            _laps.Observe(telemetry);
        }

        var usages = _laps.FuelPerLap;
        var average = LapLog.TrailingAverage(usages, options.AverageSource.WindowLaps());
        var lapsRemainingWithFuel = average > 0 ? levelLiters / average : 0;
        var lapsLeftInSession = EstimateLapsLeftInSession(telemetry, session, _laps.RecentLapSeconds());

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
            LastLapLiters = usages.Count > 0 ? usages[^1] : 0,
            AverageLiters = average,
            MinLiters = usages.Count > 0 ? usages.Min() : 0,
            MaxLiters = usages.Count > 0 ? usages.Max() : 0,
            LapsRemainingWithFuel = lapsRemainingWithFuel,
            LapsLeftInSession = lapsLeftInSession,
            FuelToFinishLiters = fuelToFinish,
            FuelDeltaLiters = fuelDelta,
            TankCapacityLiters = ResolveTankCapacity(session, levelLiters, levelPct),
            UnitSystem = Units.Read(telemetry),
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

    /// <summary>
    /// Laps left to run. Prefers iRacing's own lap counter; for a timed session (where that counter
    /// reports "no limit") it falls back to the session clock divided by lap time — the player's own
    /// recent racing laps, never an out lap or the formation lap. Rounded up because a timed race
    /// ends when the leader completes the lap in progress as the clock hits zero — the part-lap
    /// still has to be fuelled.
    /// </summary>
    private static double? EstimateLapsLeftInSession(TelemetrySnapshot telemetry, IracingSessionInfo? session, double recentRacingLapSeconds)
    {
        if (SessionClock.LapsRemaining(telemetry) is { } laps)
        {
            return laps;
        }

        var lapTime = SessionClock.ReferenceLapSeconds(telemetry, session, recentRacingLapSeconds);
        return SessionClock.TimeRemaining(telemetry) is { } secondsRemaining and > 0 && lapTime > 0
            ? Math.Ceiling(secondsRemaining / lapTime)
            : null;
    }
}
