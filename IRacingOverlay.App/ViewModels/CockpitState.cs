namespace IRacingOverlay.App.ViewModels;

public sealed class CockpitState
{
    public const int ShiftLightCount = 5;

    public required string Gear { get; init; }
    public required int ShiftLightsLit { get; init; }
    public required bool ShiftBlink { get; init; }
    public required bool AbsActive { get; init; }

    /// <summary>0 (no overlap) to 1 (fully alongside) for the nearest car on each side. See
    /// CockpitBuilder for how this is approximated — iRacing doesn't expose other cars' lateral position.</summary>
    public required double LeftProximity { get; init; }
    public required double RightProximity { get; init; }

    public static CockpitState Empty { get; } = new()
    {
        Gear = "–",
        ShiftLightsLit = 0,
        ShiftBlink = false,
        AbsActive = false,
        LeftProximity = 0,
        RightProximity = 0,
    };
}
