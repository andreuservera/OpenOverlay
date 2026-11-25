using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Which cars are out of the race for now — parked in the garage, retired or disconnected — so
/// Standings can show their rows as absent. Only a race uses it: in Practice and Qualifying the
/// table ranks best laps, and who is in the garage there is constant churn, not news.
///
/// "Out of the world" alone is not enough. CarIdxTrackSurface (and every other CarIdx* array) reads
/// -1 both for a car in the garage and for a car this client simply doesn't render: iRacing's "max
/// cars" setting hides everything past that many cars, and those cars keep racing. So a car out of
/// the world only counts as gone once the server's scoring table hasn't counted a lap for it in
/// longer than a slow lap and a pit stop take — a hidden car still racing keeps completing laps there.
///
/// Slow on purpose: a car is marked a few minutes after it leaves. An earlier second rule ("the
/// client renders fewer cars than it has before") marked it sooner, but read every hidden car as gone
/// in fields bigger than the client's limit, so far-away rows flickered dark and light as the client
/// swapped cars in and out of view — seen live in a 50-car team practice with a 20-car client.
/// Reads telemetry and the scoring table only; it has no effect on any other handling.
/// </summary>
internal sealed class GarageTracker
{
    private const int NotInWorld = -1;

    // Long enough that a reset to the pits or a tow's blink never reads as leaving.
    private const double OutSettleSeconds = 3.0;

    // No lap counted in this long means nobody is driving the car: a slow lap with a pit stop in it.
    private const double StaleLapFactor = 1.5;
    private const double StalePitAllowanceSeconds = 60.0;
    private const double FallbackLapSeconds = 120.0;

    private readonly Dictionary<int, double> _outSince = new();
    private readonly Dictionary<int, (int Laps, double Since)> _scored = new();
    private readonly HashSet<int> _inGarage = new();
    private int? _sessionNum;
    private double _lastSessionTime;

    public IReadOnlySet<int> InGarage => _inGarage;

    public void Update(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.CarIdxTrackSurface) ||
            !telemetry.HasVariable(TelemetryVarNames.SessionTime))
        {
            return;
        }

        var sessionNum = CurrentSession.Number(telemetry, session);
        var now = telemetry.GetDouble(TelemetryVarNames.SessionTime);
        // A new session, or the clock running backwards (replay), starts the counts over.
        if (_sessionNum != sessionNum || now < _lastSessionTime)
        {
            _sessionNum = sessionNum;
            _outSince.Clear();
            _scored.Clear();
            _inGarage.Clear();
        }

        _lastSessionTime = now;

        var surfaces = telemetry.GetIntArray(TelemetryVarNames.CarIdxTrackSurface);
        // The cars in the race. Without the roster every slot is taken: unused ones never enter the
        // world, so they are never marked.
        var cars = session?.DriverInfo?.Drivers
            .Where(d => !d.IsPaceCar && !d.IsGhost && d.CarIdx >= 0 && d.CarIdx < surfaces.Length)
            .Select(d => d.CarIdx)
            .ToList()
            ?? Enumerable.Range(0, surfaces.Length).ToList();

        var results = CurrentSession.Results(telemetry, session);
        var staleAfter = StaleLapFactor * ReferenceLapSeconds(results) + StalePitAllowanceSeconds;

        _inGarage.Clear();
        foreach (var carIdx in cars)
        {
            var laps = results.TryGetValue(carIdx, out var scored) ? scored.LapsComplete : -1;
            if (!_scored.TryGetValue(carIdx, out var lastScored) || lastScored.Laps != laps)
            {
                lastScored = (laps, now);
                _scored[carIdx] = lastScored;
            }

            if (surfaces[carIdx] != NotInWorld)
            {
                _outSince.Remove(carIdx);
                continue;
            }

            if (!_outSince.TryGetValue(carIdx, out var outSince))
            {
                outSince = now;
                _outSince[carIdx] = outSince;
            }

            if (now - outSince < OutSettleSeconds)
            {
                continue;
            }

            if (now - lastScored.Since >= staleAfter)
            {
                _inGarage.Add(carIdx);
            }
        }
    }

    /// <summary>The slowest best lap in the scoring table: a hidden car of the slowest class still
    /// completes a lap inside it. A fallback before anyone has set one.</summary>
    private static double ReferenceLapSeconds(Dictionary<int, SessionResultPosition> results)
    {
        var slowestBest = results.Values.Select(r => r.FastestTime).Where(t => t > 0).DefaultIfEmpty(0).Max();
        return slowestBest > 0 ? slowestBest : FallbackLapSeconds;
    }
}
