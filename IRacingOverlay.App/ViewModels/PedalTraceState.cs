namespace IRacingOverlay.App.ViewModels;

public sealed class PedalTraceState
{
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
