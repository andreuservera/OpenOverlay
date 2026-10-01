using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// The player's representative laps: the fuel burned and the time taken on each lap run from line to
/// line at racing speed. The fuel readouts average it and the session clock prices a lap with it, so
/// every projection in the app agrees on what a lap costs.
///
/// A lap only counts when all of it was a racing lap. Left out, fuel and time alike:
///   • the out lap — the window opened by getting into the car, which starts in the pit stall;
///   • any lap that touches pit road: in-laps, drive-throughs, a stop on the way past;
///   • the formation / pace lap and anything before the green or after the cool-down starts;
///   • a lap with a refuel in it, which can't be read off the level difference;
///   • the partial lap the overlay was attached in the middle of, and any stretch it wasn't
///     watching (the lap counter jumping by more than one).
/// </summary>
internal sealed class LapLog
{
    // irsdk_SessionState: 1 get in car, 2 warmup, 3 parade laps, 4 racing, 5 checkered, 6 cool down.
    private const int StateGetInCar = 1;
    private const int StateWarmup = 2;
    private const int StateParadeLaps = 3;
    private const int StateCoolDown = 6;

    // Float noise on FuelLevel is far below this; a real refuel is far above it.
    private const double RefuelToleranceLiters = 0.05;

    // How far past the line the first sample may be for its lap to still count as a whole one.
    private const float StartLineTolerance = 0.05f;

    private readonly List<double> _fuelPerLap = [];
    private readonly List<double> _lapSeconds = [];
    private int? _lap;
    private double _windowFuel;
    private double _windowStartTime;
    private bool _windowDirty;
    private bool _wasOnTrack;

    /// <summary>Litres burned on each counted lap, oldest first.</summary>
    public IReadOnlyList<double> FuelPerLap => _fuelPerLap;

    /// <summary>Seconds taken on each counted lap, oldest first. Empty without SessionTime.</summary>
    public IReadOnlyList<double> LapSeconds => _lapSeconds;

    /// <summary>Average of the last <paramref name="window"/> counted laps' times; 0 before any.</summary>
    public double RecentLapSeconds(int window = 3) => TrailingAverage(_lapSeconds, window);

    public static double TrailingAverage(IReadOnlyList<double> values, int window)
    {
        if (values.Count == 0 || window <= 0)
        {
            return 0;
        }

        var take = Math.Min(window, values.Count);
        var total = 0.0;
        for (var i = values.Count - take; i < values.Count; i++)
        {
            total += values[i];
        }

        return total / take;
    }

    public void Observe(TelemetrySnapshot telemetry)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.Lap))
        {
            return;
        }

        var lap = telemetry.GetInt(TelemetryVarNames.Lap);
        var fuel = telemetry.HasVariable(TelemetryVarNames.FuelLevel) ? telemetry.GetFloat(TelemetryVarNames.FuelLevel) : 0;
        var now = telemetry.HasVariable(TelemetryVarNames.SessionTime) ? telemetry.GetDouble(TelemetryVarNames.SessionTime) : 0;
        // A missing IsOnTrack defaults to "driving" rather than risk throwing away real laps.
        var isOnTrack = !telemetry.HasVariable(TelemetryVarNames.IsOnTrack) || telemetry.GetBool(TelemetryVarNames.IsOnTrack);
        var unrepresentative = IsOnPitRoad(telemetry) || IsOutsideRacing(telemetry);

        if (_lap is null || lap < _lap)
        {
            // First sample, or a new session reset the lap counter: start over rather than carry the
            // last session's laps into this one. Seeding _wasOnTrack from now keeps the very first
            // sample from reading as getting into the car.
            _fuelPerLap.Clear();
            _lapSeconds.Clear();
            _wasOnTrack = isOnTrack;
            Start(lap, fuel, now, dirty: unrepresentative || !IsAtStartLine(telemetry));
            return;
        }

        if (!isOnTrack)
        {
            // Garage, setup screen, spectating or a replay: no lap is being run, and the fuel load can
            // be changed freely here, so keep re-basing instead of measuring.
            _wasOnTrack = false;
            Start(lap, fuel, now, dirty: true);
            return;
        }

        var justEnteredCar = !_wasOnTrack;
        _wasOnTrack = true;

        var lapsElapsed = lap - _lap.Value;
        if (lapsElapsed <= 0)
        {
            if (justEnteredCar || fuel > _windowFuel + RefuelToleranceLiters)
            {
                // The level now is the true one however it got there (garage fuelling, a stop), but
                // this lap can no longer be measured whole: it's the out lap, or it has a refuel in it.
                Start(lap, fuel, now, dirty: true);
            }
            else if (unrepresentative)
            {
                _windowDirty = true;
            }

            return;
        }

        // Crossing the line in the pit lane makes the lap that just ended an in-lap as well.
        if (lapsElapsed == 1 && !_windowDirty && !justEnteredCar && !unrepresentative)
        {
            var used = _windowFuel - fuel;
            if (used > 0)
            {
                _fuelPerLap.Add(used);
            }

            var seconds = now - _windowStartTime;
            if (now > 0 && seconds > 0)
            {
                _lapSeconds.Add(seconds);
            }
        }

        Start(lap, fuel, now, dirty: unrepresentative);
    }

    private void Start(int lap, double fuel, double now, bool dirty)
    {
        _lap = lap;
        _windowFuel = fuel;
        _windowStartTime = now;
        _windowDirty = dirty;
    }

    private static bool IsOnPitRoad(TelemetrySnapshot telemetry) =>
        telemetry.HasVariable(TelemetryVarNames.OnPitRoad) && telemetry.GetBool(TelemetryVarNames.OnPitRoad);

    /// <summary>Before the green (getting in, warm-up, the formation and pace laps) or the cool-down
    /// lap after the flag. Practice and qualifying run in the racing state too.</summary>
    private static bool IsOutsideRacing(TelemetrySnapshot telemetry) =>
        telemetry.HasVariable(TelemetryVarNames.SessionState) &&
        telemetry.GetInt(TelemetryVarNames.SessionState) is StateGetInCar or StateWarmup or StateParadeLaps or StateCoolDown;

    /// <summary>Without LapDistPct there's no telling, and the sample is taken at face value.</summary>
    private static bool IsAtStartLine(TelemetrySnapshot telemetry)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.LapDistPct))
        {
            return true;
        }

        var pct = telemetry.GetFloat(TelemetryVarNames.LapDistPct);
        return pct is >= 0 and < StartLineTolerance;
    }
}
