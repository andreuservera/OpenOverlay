using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

public enum RadarLane
{
    Left,
    Centre,
    Right,
}

/// <summary>One car near the player: metres ahead (negative behind), and the lane it is drawn in.</summary>
public readonly record struct RadarCar(double GapMeters, RadarLane Lane, string ClassColor)
{
    /// <summary>Some part of it is level with some part of the player's car.</summary>
    public bool Alongside => Lane != RadarLane.Centre;
}

public sealed record RadarState(IReadOnlyList<RadarCar> Cars)
{
    public static RadarState Empty { get; } = new([]);
}

/// <summary>
/// The cars around the player, seen from above. iRacing gives no lateral position for other cars,
/// only how far along the lap each one is and, for the player, which sides have a car alongside
/// (CarLeftRight). So: every car within <see cref="RangeMeters"/> ahead or behind is placed by its
/// gap along the track; the closest ones level with the player go to the side CarLeftRight names,
/// and the rest are drawn in line, ahead or behind.
/// </summary>
internal static class RadarBuilder
{
    public const double RangeMeters = 20;
    public const double CarLengthMeters = 4.8;

    // Level with the player: a car length either way, plus a little for iRacing calling it a beat
    // before the noses line up.
    private const double AlongsideMeters = CarLengthMeters * 1.2;

    public static RadarState Build(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (session?.DriverInfo is not { } driverInfo ||
            CockpitBuilder.ParseTrackLengthMeters(session.WeekendInfo?.TrackLength) is not { } trackLength ||
            !telemetry.HasVariable(TelemetryVarNames.CarIdxLapDistPct))
        {
            return RadarState.Empty;
        }

        var pct = telemetry.GetFloatArray(TelemetryVarNames.CarIdxLapDistPct);
        var player = driverInfo.DriverCarIdx;
        if (player < 0 || player >= pct.Length || pct[player] < 0)
        {
            return RadarState.Empty;
        }

        var onPitRoad = telemetry.HasVariable(TelemetryVarNames.CarIdxOnPitRoad)
            ? telemetry.GetBoolArray(TelemetryVarNames.CarIdxOnPitRoad)
            : null;
        var playerInPits = onPitRoad is not null && player < onPitRoad.Length && onPitRoad[player];
        var carLeftRight = telemetry.HasVariable(TelemetryVarNames.CarLeftRight) ? telemetry.GetInt(TelemetryVarNames.CarLeftRight) : 0;

        var nearby = new List<(double Gap, string Color)>();
        foreach (var driver in driverInfo.Drivers)
        {
            var idx = driver.CarIdx;
            if (driver.IsPaceCar || idx == player || idx < 0 || idx >= pct.Length || pct[idx] < 0)
            {
                continue;
            }

            // A car in the pit lane isn't racing the one on track beside it, and the other way round.
            var inPits = onPitRoad is not null && idx < onPitRoad.Length && onPitRoad[idx];
            if (inPits != playerInPits)
            {
                continue;
            }

            var gap = TrackPosition.FoldToHalfLap((double)pct[idx] - pct[player]) * trackLength;
            if (Math.Abs(gap) <= RangeMeters)
            {
                nearby.Add((gap, ClassColorFormat.Normalize(driver.CarClassColor)));
            }
        }

        return Assign(nearby, carLeftRight);
    }

    /// <summary>
    /// Lanes for the cars within range, given CarLeftRight (irsdk_CarLeftRight: 0 off, 1 clear,
    /// 2 car left, 3 car right, 4 cars both sides, 5 two cars left, 6 two cars right). The closest
    /// cars level with the player fill the sides it names, nearest first; a car level with the player
    /// on no side iRacing knows of (a crossover bridge, a parallel bit of track) is left out.
    /// </summary>
    public static RadarState Assign(IReadOnlyList<(double Gap, string Color)> nearby, int carLeftRight)
    {
        if (nearby.Count == 0)
        {
            return RadarState.Empty;
        }

        var (left, right) = carLeftRight switch
        {
            2 => (1, 0),
            3 => (0, 1),
            4 => (1, 1),
            5 => (2, 0),
            6 => (0, 2),
            _ => (0, 0),
        };

        var cars = new List<RadarCar>(nearby.Count);
        foreach (var (gap, color) in nearby.OrderBy(car => Math.Abs(car.Gap)))
        {
            if (Math.Abs(gap) >= AlongsideMeters)
            {
                cars.Add(new RadarCar(gap, RadarLane.Centre, color));
            }
            else if (left > 0)
            {
                left--;
                cars.Add(new RadarCar(gap, RadarLane.Left, color));
            }
            else if (right > 0)
            {
                right--;
                cars.Add(new RadarCar(gap, RadarLane.Right, color));
            }
        }

        return new RadarState(cars);
    }
}
