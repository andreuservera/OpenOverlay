namespace IRacingOverlay.App.ViewModels;

public sealed class FlagState
{
    public required string Name { get; init; }
    public required string BackgroundColor { get; init; }
    public required string ForegroundColor { get; init; }
    public required bool IsCheckered { get; init; }
    public required bool IsMeatball { get; init; }

    public static FlagState None { get; } = new()
    {
        Name = "—",
        BackgroundColor = "#1A1A1A",
        ForegroundColor = "#555555",
        IsCheckered = false,
        IsMeatball = false,
    };
}
