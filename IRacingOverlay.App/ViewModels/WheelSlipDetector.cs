namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// iRacing does not expose a real "TC is cutting power right now" telemetry variable — confirmed:
/// there's no traction-control-intervention flag, and no per-wheel speed/spin variables either. This
/// is a deliberate anti-cheat limitation (the SimHub community has hit the exact same wall trying to
/// build a TC LED). This approximates "a driven wheel just lost grip" instead: in a fixed gear, RPM
/// tracks vehicle speed roughly linearly, so a wheel spinning faster than the car is actually moving
/// shows up as a sudden jump in the RPM-to-speed ratio. It's a heuristic, not a measurement — expect
/// to tune the threshold once you've actually seen it trigger (or not) while driving.
/// Stateful across ticks (keeps a rolling baseline of the "normal" ratio, one per gear — a plain gear
/// change shifts this ratio just as much as wheelspin would, so mixing them into one baseline made
/// every upshift/downshift look like a slip event, which is exactly the "flashing too much on corner
/// exits" bug reported live: corner exits are precisely when gear changes bunch up).
/// One instance per session, call from a single thread (the UI timer).
/// </summary>
internal sealed class WheelSlipDetector
{
    private const double MinSpeedMps = 3.0;
    private const double MinThrottle = 0.3;
    private const double SpikeThreshold = 1.12; // ratio jump beyond 12% above baseline counts as slip
    private const double BaselineSmoothing = 0.08; // EMA weight applied to each new non-slipping sample
    private const int RequiredConsecutiveTicks = 2; // debounce: ignore single-tick noise spikes

    private readonly Dictionary<int, double> _baselineByGear = [];
    private int _consecutiveSlipTicks;

    public bool Update(float rpm, float speedMps, float throttle, int gear)
    {
        if (gear <= 0 || speedMps < MinSpeedMps || throttle < MinThrottle || rpm <= 0)
        {
            // Not in a state where the ratio is meaningful (stationary, off-throttle, neutral/reverse) —
            // don't flag, and don't let a meaningless sample corrupt any gear's baseline.
            _consecutiveSlipTicks = 0;
            return false;
        }

        var ratio = rpm / speedMps;

        if (!_baselineByGear.TryGetValue(gear, out var baseline))
        {
            _baselineByGear[gear] = ratio;
            _consecutiveSlipTicks = 0;
            return false;
        }

        if (ratio > baseline * SpikeThreshold)
        {
            _consecutiveSlipTicks++;
        }
        else
        {
            _consecutiveSlipTicks = 0;
            // Only chase the baseline while grip looks normal, so a slip event doesn't drag the
            // baseline up with it (which would make the detector blind to sustained wheelspin).
            _baselineByGear[gear] = baseline + (ratio - baseline) * BaselineSmoothing;
        }

        return _consecutiveSlipTicks >= RequiredConsecutiveTicks;
    }
}
