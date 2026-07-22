namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// How much of a nearby car overlaps our own car's length, and *where* along that length — 0 at our
/// front bumper (top of the bar), 1 at our rear bumper (bottom). Overtaking someone sweeps
/// BandStart/BandEnd from a sliver at the top (we've just caught their rear bumper with our nose)
/// down to a sliver at the bottom (we're clearing their front) as we pass; being overtaken sweeps the
/// other way. A plain "amount alongside" scalar can't distinguish those two situations even though
/// they call for opposite reactions.
/// </summary>
public readonly record struct ProximitySide(double Amount, double BandStart, double BandEnd)
{
    public static ProximitySide None { get; } = new(0, 0, 0);
}

public sealed class CockpitState
{
    // 5 green + 5 yellow + 4 red, matching the reference dashboard design.
    public const int ShiftLightCount = 14;

    public required string Gear { get; init; }
    public required int ShiftLightsLit { get; init; }
    public required bool ShiftBlink { get; init; }
    public required bool AbsActive { get; init; }
    public required double SpeedKph { get; init; }
    public required double Rpm { get; init; }

    /// <summary>See CockpitBuilder for how this is approximated — iRacing doesn't expose other cars'
    /// lateral position at all.</summary>
    public required ProximitySide LeftProximity { get; init; }
    public required ProximitySide RightProximity { get; init; }

    public static CockpitState Empty { get; } = new()
    {
        Gear = "–",
        ShiftLightsLit = 0,
        ShiftBlink = false,
        AbsActive = false,
        SpeedKph = 0,
        Rpm = 0,
        LeftProximity = ProximitySide.None,
        RightProximity = ProximitySide.None,
    };
}
