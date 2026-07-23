namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Remembers each driver's fastest lap across a live Practice/Qualifying session, keyed by CarIdx.
/// Confirmed live: once a driver parks back in their pit stall after a timed run, iRacing's own
/// CarIdxBestLapTime/CarIdxLastLapTime telemetry for that car can drop back toward 0 — a stateless
/// read of the current tick alone can't distinguish "this car never set a time" from "this car set a
/// great time and is now just sitting still," which silently dropped parked drivers out of the
/// fastest-lap ranking. This tracker is the one thing that actually remembers which is which, tick
/// to tick, for as long as the same session (by SessionNum) stays current.
/// </summary>
internal sealed class SessionBestLapTracker
{
    private int? _sessionNum;
    private readonly Dictionary<int, double> _bestByCarIdx = new();

    /// <summary>
    /// Folds this tick's live best/last lap readings into the running cache (clearing it first if
    /// <paramref name="sessionNum"/> shows the session itself has changed, e.g. Practice ending and
    /// Qualifying beginning) and returns the up-to-date best time known for every car so far.
    /// </summary>
    public IReadOnlyDictionary<int, double> Update(int sessionNum, IEnumerable<int> carIndexes, float[]? bestLaps, float[]? lastLaps)
    {
        if (_sessionNum != sessionNum)
        {
            _sessionNum = sessionNum;
            _bestByCarIdx.Clear();
        }

        foreach (var carIdx in carIndexes)
        {
            var live = LiveBest(carIdx, bestLaps, lastLaps);
            if (live > 0 && (!_bestByCarIdx.TryGetValue(carIdx, out var cached) || live < cached))
            {
                _bestByCarIdx[carIdx] = live;
            }
        }

        return _bestByCarIdx;
    }

    private static double LiveBest(int carIdx, float[]? bestLaps, float[]? lastLaps)
    {
        var best = bestLaps is not null && carIdx >= 0 && carIdx < bestLaps.Length ? bestLaps[carIdx] : 0;
        if (best > 0)
        {
            return best;
        }

        var last = lastLaps is not null && carIdx >= 0 && carIdx < lastLaps.Length ? lastLaps[carIdx] : 0;
        return last > 0 ? last : 0;
    }
}
