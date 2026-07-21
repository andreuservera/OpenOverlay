namespace IRacingOverlay.App.ViewModels;

/// <summary>How a flag box should be rendered, beyond a plain solid color — matching how each flag
/// actually looks in real racing / iRacing's own flag icons, not just a color-coded label.</summary>
public enum FlagVisualStyle
{
    Solid,
    Checkered,
    Meatball,
    /// <summary>The "surface" flag — yellow and red diagonal stripes, used for debris/track surface hazards.</summary>
    DebrisStripes,
    /// <summary>Solid blue with a single diagonal orange stripe — the real-world "let the leader(s) by" flag.</summary>
    BlueWithOrangeStripe,
}

public sealed class FlagState
{
    public required string Name { get; init; }
    public required string BackgroundColor { get; init; }
    public required string ForegroundColor { get; init; }
    public required FlagVisualStyle Style { get; init; }

    public static FlagState None { get; } = new()
    {
        Name = "—",
        BackgroundColor = "#1A1A1A",
        ForegroundColor = "#555555",
        Style = FlagVisualStyle.Solid,
    };
}
