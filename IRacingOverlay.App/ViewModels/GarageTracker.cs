using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Which cars are out of the session for now — parked in the garage, retired or disconnected — so
/// Standings can show their rows as absent.
///
/// "Out of the world" alone is not enough. CarIdxTrackSurface (and every other CarIdx* array) reads
/// -1 both for a car in the garage and for a car this client simply doesn't render: iRacing's "max
/// cars" setting hides everything past that many cars, and those cars keep racing. So a car out of
/// the world only counts as gone with a second sign that nobody is driving it:
/// <list type="bullet">
/// <item>the client is rendering fewer cars than it has this session, and has been for a while — a
/// client at its limit swaps a hidden car in for any that leaves, so a field that stays thinner means
/// the missing cars really left; or</item>
/// <item>the server's scoring table hasn't counted a lap for the car in longer than a lap and a pit
/// stop take — a hidden car still racing keeps completing laps there.</item>
/// </list>
/// Reads telemetry and the scoring table only; it has no effect on any other handling.
/// </summary>
internal sealed class GarageTracker
{
    private const int NotInWorld = -1;

    // Long enough that a reset to the pits or a tow's blink never reads as leaving.
    private const double OutSettleSeconds = 3.0;

    // How long the field has to stay thinner before the missing cars are taken as gone, so a client
    // at its limit still swapping a hidden car in can't make every hidden car look parked.
    private const double ThinnedSettleSeconds = 10.0;

    // No lap counted in this long means nobody is driving the car: a slow lap with a pit stop in it.
    private const double StaleLapFactor = 1.5;
    private const double StalePitAllowanceSeconds = 60.0;
    private const double FallbackLapSeconds = 120.0;

    private readonly Dictionary<int, double> _outSince = new();
    private readonly Dictionary<int, (int Laps, double Since)> _scored = new();
    private readonly HashSet<int> _inGarage = new();
    private int _mostInWorld;
    private double? _thinnedSince;
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
            _mostInWorld = 0;
            _thinnedSince = null;
        }

        _lastSessionTime = now;

        var surfaces = telemetry.GetIntArray(TelemetryVarNames.CarIdxTrackSurface);
        // The cars in the race. Without the roster every slot is taken: unused ones never enter the
        // world, so they change no count. The pace car must stay out of the count — it leaving the
        // track would otherwise read as the field thinning.
        var cars = session?.DriverInfo?.Drivers
            .Where(d => !d.IsPaceCar && !d.IsGhost && d.CarIdx >= 0 && d.CarIdx < surfaces.Length)
            .Select(d => d.CarIdx)
            .ToList()
            ?? Enumerable.Range(0, surfaces.Length).ToList();

        var inWorld = cars.Count(carIdx => surfaces[carIdx] != NotInWorld);
        _mostInWorld = Math.Max(_mostInWorld, inWorld);
        _thinnedSince = inWorld < _mostInWorld ? _thinnedSince ?? now : null;
        var fieldThinned = _thinnedSince is { } thinnedAt && now - thinnedAt >= ThinnedSettleSeconds;

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

            if (fieldThinned || now - lastScored.Since >= staleAfter)
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
