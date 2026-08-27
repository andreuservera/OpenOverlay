namespace IRacingOverlay.App.ViewModels;

public sealed class PedalTraceState
{
    public required double Throttle { get; init; }
    public required double Brake { get; init; }
    public required double Clutch { get; init; }
    public required IReadOnlyList<double> ThrottleHistory { get; init; }
    public required IReadOnlyList<double> BrakeHistory { get; init; }

    /// <summary>Per-sample ABS state, index-aligned with <see cref="BrakeHistory"/> — lets the trace
    /// recolor only the stretch of the brake line where ABS was actually intervening.</summary>
    public required IReadOnlyList<bool> AbsHistory { get; init; }

    public static PedalTraceState Empty { get; } = new()
    {
        Throttle = 0,
        Brake = 0,
        Clutch = 0,
        ThrottleHistory = [],
        BrakeHistory = [],
        AbsHistory = [],
    };
}
