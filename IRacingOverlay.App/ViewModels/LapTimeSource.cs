using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Every car's best and last lap, merged from the two places iRacing keeps them.
///
/// The CarIdx* telemetry arrays are event-driven: they only carry a time once this client has seen
/// that car cross the line, so attaching the overlay to a session already underway leaves them at
/// -1 for everyone and the times only start appearing as drivers complete further laps — the
/// reported "no BEST for anyone unless somebody sets a new time". The session YAML's
/// ResultsPositions table is the server's own scoring record and is already complete when the
/// overlay connects, so it's what fills in everything that happened beforehand.
///
/// Telemetry still wins where it has something, since it updates every tick rather than at scoring
/// events; for the best lap the two are compared and the genuinely faster one kept, which also
/// covers iRacing dropping a parked car's live best back toward zero.
/// </summary>
internal sealed class LapTimeSource
{
    private readonly float[]? _liveBest;
    private readonly float[]? _liveLast;
    private readonly IReadOnlyDictionary<int, SessionResultPosition> _results;

    public LapTimeSource(float[]? liveBest, float[]? liveLast, IReadOnlyDictionary<int, SessionResultPosition> results)
    {
        _liveBest = liveBest;
        _liveLast = liveLast;
        _results = results;
    }

    public double Best(int carIdx)
    {
        var live = Live(_liveBest, carIdx);
        var scored = _results.TryGetValue(carIdx, out var result) ? result.FastestTime : 0;
        return Faster(live, scored);
    }

    public double Last(int carIdx)
    {
        var live = Live(_liveLast, carIdx);
        return live > 0
            ? live
            : _results.TryGetValue(carIdx, out var result) && result.LastTime > 0 ? result.LastTime : 0;
    }

    /// <summary>Fastest lap set by anyone, or 0 if nobody has one. The reference "how long is a lap
    /// here" figure, and the benchmark the fastest-lap highlight is measured against.</summary>
    public double FastestOf(IEnumerable<int> carIndexes)
    {
        var fastest = 0.0;
        foreach (var carIdx in carIndexes)
        {
            fastest = Faster(fastest, Best(carIdx));
        }

        return fastest;
    }

    private static double Live(float[]? values, int carIdx) =>
        values is not null && carIdx >= 0 && carIdx < values.Length && values[carIdx] > 0 ? values[carIdx] : 0;

    private static double Faster(double a, double b)
    {
        if (a <= 0)
        {
            return b > 0 ? b : 0;
        }

        return b > 0 && b < a ? b : a;
    }
}
