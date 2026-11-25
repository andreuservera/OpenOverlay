using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>A completed pit stop: the lap the car entered pit road on, and its pit-lane time.</summary>
public readonly record struct PitStop(int Lap, double Seconds);

/// <summary>
/// Remembers each car's most recent pit stop for the running session, keyed by CarIdx. A stop is
/// timed from pit-road entry to pit-road exit on the session clock, and only counts when the car
/// was seen driving on track before it entered — so leaving the pits at the start of a session,
/// being towed, or attaching to a car already in the lane never reads as a stop. Reads telemetry
/// only; it has no effect on any other pit handling.
///
/// Also which cars are on an out-lap: from leaving pit road until they complete the lap they left
/// on. Any exit counts, a stop or a car just out of the garage (it appears in its stall, on pit
/// road), but only one seen in the lane: attaching to a car already out never guesses.
/// </summary>
internal sealed class PitStopTracker
{
    private const int NotInWorld = -1;
    private const int InPitStall = 1;

    private enum Phase { Unknown, Driving, InLane }

    private readonly Dictionary<int, (Phase Phase, double EnteredAt, int Lap)> _cars = new();
    private readonly Dictionary<int, PitStop> _last = new();
    private readonly HashSet<int> _seenInLane = new();
    private readonly Dictionary<int, int> _outLapOf = new();
    private readonly HashSet<int> _onOutLap = new();
    private int? _sessionNum;
    private double _lastSessionTime;

    public IReadOnlyDictionary<int, PitStop> LastStops => _last;

    /// <summary>Cars out of the pits on the lap they left on, as of the last update. A car hidden
    /// by the client's max-cars limit drops out of it while hidden.</summary>
    public IReadOnlySet<int> OnOutLap => _onOutLap;

    public void Update(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.CarIdxOnPitRoad) ||
            !telemetry.HasVariable(TelemetryVarNames.CarIdxLap) ||
            !telemetry.HasVariable(TelemetryVarNames.SessionTime))
        {
            return;
        }

        var sessionNum = CurrentSession.Number(telemetry, session);
        var now = telemetry.GetDouble(TelemetryVarNames.SessionTime);
        // A new session, or the clock running backwards (replay), makes every open stop meaningless.
        if (_sessionNum != sessionNum || now < _lastSessionTime)
        {
            _sessionNum = sessionNum;
            _cars.Clear();
            _last.Clear();
            _seenInLane.Clear();
            _outLapOf.Clear();
        }

        _onOutLap.Clear();

        _lastSessionTime = now;

        var onPitRoad = telemetry.GetBoolArray(TelemetryVarNames.CarIdxOnPitRoad);
        var laps = telemetry.GetIntArray(TelemetryVarNames.CarIdxLap);
        var surfaces = telemetry.HasVariable(TelemetryVarNames.CarIdxTrackSurface)
            ? telemetry.GetIntArray(TelemetryVarNames.CarIdxTrackSurface)
            : null;

        var count = Math.Min(onPitRoad.Length, laps.Length);
        for (var carIdx = 0; carIdx < count; carIdx++)
        {
            var inWorld = surfaces is null || carIdx >= surfaces.Length || surfaces[carIdx] != NotInWorld;
            var state = _cars.GetValueOrDefault(carIdx);

            if (!inWorld)
            {
                state = default;
            }
            else if (onPitRoad[carIdx])
            {
                if (state.Phase == Phase.Driving)
                {
                    state = (Phase.InLane, now, laps[carIdx]);
                }
            }
            else
            {
                if (state.Phase == Phase.InLane)
                {
                    _last[carIdx] = new PitStop(state.Lap, now - state.EnteredAt);
                }

                state = (Phase.Driving, 0, 0);
            }

            _cars[carIdx] = state;
            TrackOutLap(carIdx, inWorld, onPitRoad[carIdx] || (surfaces is not null && carIdx < surfaces.Length && surfaces[carIdx] == InPitStall), laps[carIdx]);
        }
    }

    private void TrackOutLap(int carIdx, bool inWorld, bool inLane, int lap)
    {
        if (!inWorld)
        {
            // Towed, parked or just hidden: the lane has to be seen again. An out-lap already under
            // way is kept, so a car that drops out of view comes back with it if still on it.
            _seenInLane.Remove(carIdx);
            return;
        }

        if (inLane)
        {
            _seenInLane.Add(carIdx);
            _outLapOf.Remove(carIdx);
            return;
        }

        if (_seenInLane.Remove(carIdx))
        {
            _outLapOf[carIdx] = lap;
        }

        if (!_outLapOf.TryGetValue(carIdx, out var outLap))
        {
            return;
        }

        if (lap > outLap)
        {
            _outLapOf.Remove(carIdx);
        }
        else if (lap == outLap)
        {
            _onOutLap.Add(carIdx);
        }
    }
}
