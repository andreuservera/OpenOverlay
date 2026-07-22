namespace IRacingOverlay.App.ViewModels;

public sealed class PedalTraceState
{
    public required double Throttle { get; init; }
    public required double Brake { get; init; }
    public required double Clutch { get; init; }
    public required IReadOnlyList<double> ThrottleHistory { get; init; }
    public required IReadOnlyList<double> BrakeHistory { get; init; }

    public static PedalTraceState Empty { get; } = new()
    {
        Throttle = 0,
        Brake = 0,
        Clutch = 0,
        ThrottleHistory = [],
        BrakeHistory = [],
    };
}
