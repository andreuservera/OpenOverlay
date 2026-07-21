namespace IRacingOverlay.App.ViewModels;

public sealed class RelativeRow
{
    public required int CarIdx { get; init; }
    public required string Name { get; init; }
    public required string CarNumber { get; init; }
    public required bool IsPlayer { get; init; }
    public required double GapSeconds { get; init; } // negative = ahead of player, positive = behind
    public required bool OnPitRoad { get; init; }
    public string ClassColor { get; init; } = "#FFFFFF";

    public string GapDisplay => IsPlayer
        ? "—"
        : (GapSeconds <= 0 ? $"-{Math.Abs(GapSeconds):0.0}" : $"+{GapSeconds:0.0}");

    public string RowBackground => IsPlayer ? "#4433AAFF" : "Transparent";
}
