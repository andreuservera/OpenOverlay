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
/// </summary>
internal sealed class PitStopTracker
{
    private const int NotInWorld = -1;

    private enum Phase { Unknown, Driving, InLane }

    private readonly Dictionary<int, (Phase Phase, double EnteredAt, int Lap)> _cars = new();
    private readonly Dictionary<int, PitStop> _last = new();
    private int? _sessionNum;
    private double _lastSessionTime;

    public IReadOnlyDictionary<int, PitStop> LastStops => _last;

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
        }

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
        }
    }
}
