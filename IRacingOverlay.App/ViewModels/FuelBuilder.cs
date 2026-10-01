using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Turns iRacing's live fuel level into "how many laps of fuel do I have left, and is that enough
/// to finish." Unlike iRacing's own instantaneous FuelUsePerHour (which jumps around lap to lap
/// depending on how that particular lap was driven), this tracks the fuel level at each lap
/// boundary and averages actual consumption across every racing lap this session — a stable
/// number a driver can actually plan a pit strategy around. Which laps count (no out laps, pit laps
/// or formation lap) is <see cref="LapLog"/>'s call, shared with the Fuel Calculator.
/// </summary>
internal sealed class FuelBuilder
{
    private readonly LapLog _laps;
    private readonly bool _ownsLog;

    /// <summary>Keeps its own lap log, fed by <see cref="Build"/>.</summary>
    public FuelBuilder()
        : this(new LapLog(), ownsLog: true)
    {
    }

    /// <summary>Reads a lap log someone else keeps up to date.</summary>
    public FuelBuilder(LapLog laps)
        : this(laps, ownsLog: false)
    {
    }

    private FuelBuilder(LapLog laps, bool ownsLog)
    {
        _laps = laps;
        _ownsLog = ownsLog;
    }

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

        if (_ownsLog)
        {
            _laps.Observe(telemetry);
        }

        var perLapLiters = LapLog.TrailingAverage(_laps.FuelPerLap, int.MaxValue);
        var lapsOfFuelRemaining = perLapLiters > 0 ? levelLiters / perLapLiters : 0;

        return new FuelState
        {
            LevelLiters = levelLiters,
            LevelPct = levelPct,
            PerLapLiters = perLapLiters,
            LapsOfFuelRemaining = lapsOfFuelRemaining,
            LapsRemainingInSession = SessionClock.LapsRemaining(telemetry),
            UnitSystem = Units.Read(telemetry),
        };
    }
}
