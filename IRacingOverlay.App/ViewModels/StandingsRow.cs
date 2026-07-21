namespace IRacingOverlay.App.ViewModels;

public sealed class StandingsRow
{
    public required int CarIdx { get; init; }
    public required int Position { get; init; }
    public required int ClassPosition { get; init; }
    public required string Name { get; init; }
    public required string CarNumber { get; init; }
    public required bool IsPlayer { get; init; }
    public required bool OnPitRoad { get; init; }
    public required int CurrentLap { get; init; }
    public required double GapToLeaderSeconds { get; init; }
    public required double LastLapTime { get; init; }
    public required double BestLapTime { get; init; }
    public required bool IsMultiClass { get; init; }
    public string ClassColor { get; init; } = "#FFFFFF";

    public string GapDisplay => Position == 1 ? "Leader" : $"+{GapToLeaderSeconds:0.0}";

    public string PositionDisplay => IsMultiClass ? $"{Position} ({ClassPosition})" : Position.ToString();

    public string LastLapDisplay => FormatLapTime(LastLapTime);

    public string BestLapDisplay => FormatLapTime(BestLapTime);

    public string RowBackground => IsPlayer ? "#4433AAFF" : "Transparent";

    private static string FormatLapTime(double seconds) =>
        seconds > 0 ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.fff") : "—";
}
