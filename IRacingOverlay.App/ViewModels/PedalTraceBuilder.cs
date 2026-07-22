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
    private const int HistoryLength = 50; // ~5s at the app's 100ms tick rate

    private readonly Queue<double> _throttleHistory = new(HistoryLength);
    private readonly Queue<double> _brakeHistory = new(HistoryLength);

    public PedalTraceState Build(TelemetrySnapshot telemetry)
    {
        var throttle = ReadPedal(telemetry, TelemetryVarNames.Throttle);
        var brake = ReadPedal(telemetry, TelemetryVarNames.Brake);
        var clutch = ReadClutch(telemetry);

        Push(_throttleHistory, throttle);
        Push(_brakeHistory, brake);

        return new PedalTraceState
        {
            Throttle = throttle,
            Brake = brake,
            Clutch = clutch,
            ThrottleHistory = _throttleHistory.ToArray(),
            BrakeHistory = _brakeHistory.ToArray(),
        };
    }

    private static void Push(Queue<double> history, double value)
    {
        history.Enqueue(value);
        while (history.Count > HistoryLength)
        {
            history.Dequeue();
        }
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
