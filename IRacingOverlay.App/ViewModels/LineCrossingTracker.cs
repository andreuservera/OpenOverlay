using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// When each car crossed the line to complete each lap of the running session, on the session clock:
/// the timing a race classification is built from. A gap to the class leader is read at the line —
/// how long after the first car of the class to complete that lap this car completed it — so it
/// changes only as the car crosses, never mid-lap.
///
/// Telemetry arrives at 10 Hz, when a car can cover 8 m between ticks, so the crossing is placed
/// between the two ticks around it by how far each was from the line. Must see every tick: a missed
/// crossing is not made up later.
/// </summary>
internal sealed class LineCrossingTracker
{
    private readonly Dictionary<int, Dictionary<int, double>> _crossings = new();
    private readonly Dictionary<int, (int Lap, double Time)> _latest = new();
    private readonly Dictionary<int, (int Laps, double Pct, double Time)> _previous = new();
    private readonly Dictionary<int, int> _lapsWhenFirstSeen = new();
    private int? _sessionNum;
    private double _lastSessionTime;

    /// <summary>Laps completed per car: CarIdxLapCompleted, or laps started where a build lacks it.
    /// Either steps up at the line, which is all the timing needs.</summary>
    public static int[]? LapsCompleted(TelemetrySnapshot telemetry) =>
        telemetry.HasVariable(TelemetryVarNames.CarIdxLapCompleted) ? telemetry.GetIntArray(TelemetryVarNames.CarIdxLapCompleted)
        : telemetry.HasVariable(TelemetryVarNames.CarIdxLap) ? telemetry.GetIntArray(TelemetryVarNames.CarIdxLap)
        : null;

    public void Update(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (LapsCompleted(telemetry) is not { } laps ||
            !telemetry.HasVariable(TelemetryVarNames.CarIdxLapDistPct) ||
            !telemetry.HasVariable(TelemetryVarNames.SessionTime))
        {
            return;
        }

        var sessionNum = CurrentSession.Number(telemetry, session);
        var now = telemetry.GetDouble(TelemetryVarNames.SessionTime);
        // A new session, or the clock running backwards (replay), starts the timing over.
        if (_sessionNum != sessionNum || now < _lastSessionTime)
        {
            _sessionNum = sessionNum;
            _crossings.Clear();
            _latest.Clear();
            _previous.Clear();
            _lapsWhenFirstSeen.Clear();
        }

        _lastSessionTime = now;

        var lapDistPct = telemetry.GetFloatArray(TelemetryVarNames.CarIdxLapDistPct);
        var count = Math.Min(laps.Length, lapDistPct.Length);
        for (var carIdx = 0; carIdx < count; carIdx++)
        {
            var completed = laps[carIdx];
            if (completed < 0)
            {
                // Out of the world (towed, disconnected): when it is back, its next tick is not one
                // to time a crossing from.
                _previous.Remove(carIdx);
                continue;
            }

            var pct = (double)lapDistPct[carIdx];
            if (!_previous.TryGetValue(carIdx, out var previous))
            {
                _lapsWhenFirstSeen.TryAdd(carIdx, completed);
            }
            else if (completed == previous.Laps + 1)
            {
                if (!_crossings.TryGetValue(carIdx, out var byLap))
                {
                    byLap = new Dictionary<int, double>();
                    _crossings[carIdx] = byLap;
                }

                var crossedAt = CrossingTime(previous.Pct, previous.Time, pct, now);
                byLap[completed] = crossedAt;
                _latest[carIdx] = (completed, crossedAt);
            }

            _previous[carIdx] = (completed, pct, now);
        }
    }

    /// <summary>The most recent crossing seen for <paramref name="carIdx"/>: the lap it completed and when.</summary>
    public (int Lap, double Time)? LastCrossing(int carIdx) =>
        _latest.TryGetValue(carIdx, out var latest) ? latest : null;

    /// <summary>
    /// When the first of <paramref name="cars"/> completed <paramref name="lap"/> — the leader of that
    /// group at the line. Null unless every one of them was being watched before it could have
    /// completed that lap: attached mid-race, the real first car may have crossed unseen.
    /// </summary>
    public double? FirstCrossing(IReadOnlyCollection<int> cars, int lap)
    {
        double? first = null;
        foreach (var carIdx in cars)
        {
            if (_lapsWhenFirstSeen.TryGetValue(carIdx, out var seenAt) && seenAt >= lap)
            {
                return null;
            }

            if (_crossings.TryGetValue(carIdx, out var byLap) && byLap.TryGetValue(lap, out var time) && time < (first ?? double.MaxValue))
            {
                first = time;
            }
        }

        return first;
    }

    private static double CrossingTime(double previousPct, double previousTime, double pct, double now)
    {
        // Wrapped from the end of the lap to its start between the two ticks: place the line in between.
        if (previousPct > 0.5 && pct is >= 0 and < 0.5)
        {
            var toLine = 1 - previousPct;
            return previousTime + toLine / (toLine + pct) * (now - previousTime);
        }

        return now;
    }
}
