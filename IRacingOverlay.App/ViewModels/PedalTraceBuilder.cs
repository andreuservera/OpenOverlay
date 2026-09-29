using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Tracks throttle/brake/clutch plus a short rolling history for a scrolling trace — the classic way
/// to see trail-braking (throttle and brake overlapping) and whether pedal inputs are smooth or
/// stabbed. Unlike the other Builders this is instance state, not a static pure function: a trace
/// inherently needs to remember recent samples across ticks, so one instance is created once (in
/// MainWindow, alongside the connection) and reused every tick.
/// </summary>
internal sealed class PedalTraceBuilder
{
    private const double HistorySeconds = 5.0;

    // iRacing publishes telemetry at a fixed 60 ticks per second.
    private const int TicksPerSecond = 60;
    private const int WindowTicks = (int)(HistorySeconds * TicksPerSecond);

    private readonly Queue<(int Tick, double Throttle, double Brake, double Clutch, bool Abs)> _samples = new();
    private PedalTraceState? _last;
    private int _lastTick;

    // One sample per sim tick, positioned by that tick: sampling on UI timer ticks (the old way)
    // spaced the trace by however evenly the timer happened to fire, which read as stutter.
    public PedalTraceState Build(TelemetrySnapshot telemetry)
    {
        var throttle = ReadPedal(telemetry, TelemetryVarNames.Throttle);
        var brake = ReadPedal(telemetry, TelemetryVarNames.Brake);
        var clutch = ReadClutch(telemetry);
        var abs = telemetry.HasVariable(TelemetryVarNames.BrakeAbsActive) && telemetry.GetBool(TelemetryVarNames.BrakeAbsActive);
        var tick = telemetry.TickCount;

        if (_last is not null)
        {
            if (tick == _lastTick)
            {
                return _last; // same sim tick, same data: nothing to add or redraw
            }

            if (tick < _lastTick)
            {
                _samples.Clear(); // sim restarted or a replay jumped back
            }
        }

        _samples.Enqueue((tick, throttle, brake, clutch, abs));
        while (_samples.Peek().Tick < tick - WindowTicks)
        {
            _samples.Dequeue();
        }

        _lastTick = tick;
        return _last = Snapshot(throttle, brake, clutch, tick);
    }

    private PedalTraceState Snapshot(double throttle, double brake, double clutch, int now)
    {
        var count = _samples.Count;
        var throttleHistory = new double[count];
        var brakeHistory = new double[count];
        var clutchHistory = new double[count];
        var absHistory = new bool[count];
        var positions = new double[count];
        var i = 0;
        foreach (var sample in _samples)
        {
            throttleHistory[i] = sample.Throttle;
            brakeHistory[i] = sample.Brake;
            clutchHistory[i] = sample.Clutch;
            absHistory[i] = sample.Abs;
            positions[i] = 1 - ((now - sample.Tick) / (double)WindowTicks);
            i++;
        }

        return new PedalTraceState
        {
            Throttle = throttle,
            Brake = brake,
            Clutch = clutch,
            ThrottleHistory = throttleHistory,
            BrakeHistory = brakeHistory,
            ClutchHistory = clutchHistory,
            AbsHistory = absHistory,
            Positions = positions,
        };
    }

    private static double ReadPedal(TelemetrySnapshot telemetry, string name) =>
        telemetry.HasVariable(name) ? Math.Clamp(telemetry.GetFloat(name), 0, 1) : 0;

    // iRacing's Clutch variable is inverted relative to Throttle/Brake: 1.0 = fully engaged
    // (pedal released), 0.0 = fully disengaged (pedal to the floor). Flip it so the trace shows
    // "how much the pedal is pressed" like the other two — otherwise autoclutch, which leaves the
    // pedal released almost all the time, reads as a constant 100% clutch.
    private static double ReadClutch(TelemetrySnapshot telemetry) =>
        telemetry.HasVariable(TelemetryVarNames.Clutch) ? 1 - Math.Clamp(telemetry.GetFloat(TelemetryVarNames.Clutch), 0, 1) : 0;
}
