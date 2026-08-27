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

    private readonly Queue<double> _throttleHistory = new();
    private readonly Queue<double> _brakeHistory = new();
    private readonly Queue<bool> _absHistory = new();

    // Sample count needed for a HistorySeconds-wide trace scales with however fast this Builder is
    // actually being ticked (the "critical refresh rate" combo) — fixing this at a sample COUNT
    // (as before, tuned for the old 100ms/10Hz default) silently shrank the visible time window to a
    // fraction of a second once the refresh rate was raised toward 60Hz, which is what read as
    // "stutter": the same 50 samples then only covered ~0.8s, so every new tick visibly lurched the
    // whole trace instead of scrolling it smoothly.
    public PedalTraceState Build(TelemetrySnapshot telemetry, double tickIntervalMs)
    {
        var maxSamples = Math.Max(2, (int)Math.Round(HistorySeconds * 1000.0 / Math.Max(1.0, tickIntervalMs)));

        var throttle = ReadPedal(telemetry, TelemetryVarNames.Throttle);
        var brake = ReadPedal(telemetry, TelemetryVarNames.Brake);
        var clutch = ReadClutch(telemetry);
        var abs = telemetry.HasVariable(TelemetryVarNames.BrakeAbsActive) && telemetry.GetBool(TelemetryVarNames.BrakeAbsActive);

        Push(_throttleHistory, throttle, maxSamples);
        Push(_brakeHistory, brake, maxSamples);
        Push(_absHistory, abs, maxSamples);

        return new PedalTraceState
        {
            Throttle = throttle,
            Brake = brake,
            Clutch = clutch,
            ThrottleHistory = _throttleHistory.ToArray(),
            BrakeHistory = _brakeHistory.ToArray(),
            AbsHistory = _absHistory.ToArray(),
        };
    }

    private static void Push<T>(Queue<T> history, T value, int maxSamples)
    {
        history.Enqueue(value);
        while (history.Count > maxSamples)
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
