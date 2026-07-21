namespace IRacingOverlay.App.ViewModels;

public sealed class CockpitState
{
    public const int ShiftLightCount = 5;

    public required string Gear { get; init; }
    public required int ShiftLightsLit { get; init; }
    public required bool ShiftBlink { get; init; }
    public required bool AbsActive { get; init; }

    /// <summary>iRacing has no real TC-intervention telemetry (confirmed — a deliberate anti-cheat
    /// limitation). This is a wheelspin heuristic (see WheelSlipDetector) standing in for it, not a
    /// direct read of the car's actual TC system.</summary>
    public required bool TcActive { get; init; }

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
        TcActive = false,
        LeftProximity = 0,
        RightProximity = 0,
    };
}
