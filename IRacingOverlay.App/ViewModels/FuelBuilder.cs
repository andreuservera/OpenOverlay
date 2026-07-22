using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Turns iRacing's live fuel level into "how many laps of fuel do I have left, and is that enough
/// to finish." Unlike iRacing's own instantaneous FuelUsePerHour (which jumps around lap to lap
/// depending on how that particular lap was driven), this tracks the fuel level at each lap
/// boundary and averages actual consumption across every completed lap this session — a stable
/// number a driver can actually plan a pit strategy around. Unlike the other Builders this is
/// instance state, not a static pure function: the running average inherently needs to remember
/// fuel level across lap boundaries, so one instance is created once (in MainWindow, alongside the
/// connection) and reused every tick.
/// </summary>
internal sealed class FuelBuilder
{
    // iRacing reports an implausibly large lap count (well beyond any real race distance) rather
    // than a sentinel like -1 when a session has no lap limit (timed or open practice/qualify) —
    // treat anything past a real race's length as "no limit" instead of a literal number of laps.
    private const int NoLapLimitThreshold = 20_000;

    private int? _lapAtLastSample;
    private double _lapStartFuelLevel;
    private double _totalFuelUsedAcrossCompletedLaps;
    private int _completedLapCount;

    public FuelState Build(TelemetrySnapshot telemetry)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.FuelLevel))
        {
            return FuelState.Empty;
        }

        var levelLiters = telemetry.GetFloat(TelemetryVarNames.FuelLevel);
        var levelPct = telemetry.HasVariable(TelemetryVarNames.FuelLevelPct)
            ? telemetry.GetFloat(TelemetryVarNames.FuelLevelPct)
            : 0;

        if (telemetry.HasVariable(TelemetryVarNames.Lap))
        {
            TrackLapConsumption(telemetry.GetInt(TelemetryVarNames.Lap), levelLiters);
        }

        var perLapLiters = _completedLapCount > 0 ? _totalFuelUsedAcrossCompletedLaps / _completedLapCount : 0;
        var lapsOfFuelRemaining = perLapLiters > 0 ? levelLiters / perLapLiters : 0;

        int? lapsRemaining = null;
        if (telemetry.HasVariable(TelemetryVarNames.SessionLapsRemain))
        {
            var raw = telemetry.GetInt(TelemetryVarNames.SessionLapsRemain);
            if (raw is > 0 and < NoLapLimitThreshold)
            {
                lapsRemaining = raw;
            }
        }

        return new FuelState
        {
            LevelLiters = levelLiters,
            LevelPct = levelPct,
            PerLapLiters = perLapLiters,
            LapsOfFuelRemaining = lapsOfFuelRemaining,
            LapsRemainingInSession = lapsRemaining,
        };
    }

    /// <summary>
    /// Records the fuel level at the start of each lap and, once the lap number advances, folds the
    /// fuel burned since that starting level into the running total/count average. Weighting by
    /// however many laps actually elapsed (rather than always +1) keeps the average correct even if
    /// a tick is missed and the lap counter jumps by more than one. A non-positive delta means fuel
    /// went up (a pit stop refuel) or stayed flat rather than being burned — that lap is excluded so
    /// a refuel doesn't corrupt the average with a nonsensical or negative consumption figure.
    /// </summary>
    private void TrackLapConsumption(int currentLap, double levelLiters)
    {
        if (_lapAtLastSample is null || currentLap < _lapAtLastSample)
        {
            // First sample this session, or the lap counter went backwards (new session/reset) —
            // (re)start tracking from here instead of carrying over stale data.
            _lapAtLastSample = currentLap;
            _lapStartFuelLevel = levelLiters;
            _totalFuelUsedAcrossCompletedLaps = 0;
            _completedLapCount = 0;
            return;
        }

        var lapsElapsed = currentLap - _lapAtLastSample.Value;
        if (lapsElapsed <= 0)
        {
            return;
        }

        var used = _lapStartFuelLevel - levelLiters;
        if (used > 0)
        {
            _totalFuelUsedAcrossCompletedLaps += used;
            _completedLapCount += lapsElapsed;
        }

        _lapAtLastSample = currentLap;
        _lapStartFuelLevel = levelLiters;
    }
}
