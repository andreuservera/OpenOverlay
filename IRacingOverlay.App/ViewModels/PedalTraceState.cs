using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

public sealed class PedalTraceState
{
    /// <summary>"R", "N", the gear number, or a dash.</summary>
    public string Gear { get; init; } = "–";

    /// <summary>Metres per second; null when the sim doesn't report it.</summary>
    public double? SpeedMs { get; init; }

    /// <summary>Radians at the rim, positive turning left; null when not reported.</summary>
    public double? SteeringRad { get; init; }

    public UnitSystem UnitSystem { get; init; }

    public string SpeedDisplay => SpeedMs is { } ms
        ? Math.Round(Units.SpeedFromMs(Math.Abs(ms), UnitSystem)).ToString("0", CultureInfo.InvariantCulture)
        : "—";

    /// <summary>KPH or MPH, as the label under the speed.</summary>
    public string SpeedUnitDisplay => UnitSystem == UnitSystem.Imperial ? "MPH" : "KPH";

    /// <summary>The icon's rotation in screen degrees: clockwise positive, so a left turn turns it
    /// anticlockwise.</summary>
    public double SteeringIconAngle => -(SteeringRad ?? 0) * 180 / Math.PI;

    public static string Percent(double pedal) => Math.Round(Math.Clamp(pedal, 0, 1) * 100).ToString("0", CultureInfo.InvariantCulture);

    public required double Throttle { get; init; }
    public required double Brake { get; init; }
    public required double Clutch { get; init; }
    public required IReadOnlyList<double> ThrottleHistory { get; init; }
    public required IReadOnlyList<double> BrakeHistory { get; init; }
    public required IReadOnlyList<double> ClutchHistory { get; init; }

    /// <summary>Per-sample ABS state, index-aligned with <see cref="BrakeHistory"/> — lets the trace
    /// recolor only the stretch of the brake line where ABS was actually intervening.</summary>
    public required IReadOnlyList<bool> AbsHistory { get; init; }

    /// <summary>Where each sample sits across the trace, 0 = the oldest edge, 1 = now. Taken from
    /// the sim's own tick clock, so the scroll speed never depends on how evenly the UI ran.</summary>
    public required IReadOnlyList<double> Positions { get; init; }

    public static PedalTraceState Empty { get; } = new()
    {
        Throttle = 0,
        Brake = 0,
        Clutch = 0,
        ThrottleHistory = [],
        BrakeHistory = [],
        ClutchHistory = [],
        AbsHistory = [],
        Positions = [],
    };
}
