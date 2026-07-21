using System.Globalization;

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
    public required int IRating { get; init; }
    public required string LicString { get; init; }
    public required bool IsSessionFastestLap { get; init; }
    public string ClassColor { get; init; } = "#FFFFFF";

    // Formatted with InvariantCulture throughout: this machine's locale uses a comma decimal
    // separator, which silently turned "+0.0" into "+0,0" in the live UI — a real display bug.
    public string GapDisplay => Position == 1 ? "Leader" : $"+{GapToLeaderSeconds.ToString("0.0", CultureInfo.InvariantCulture)}";

    public string PositionDisplay => IsMultiClass ? $"{Position} ({ClassPosition})" : Position.ToString(CultureInfo.InvariantCulture);

    public string LastLapDisplay => FormatLapTime(LastLapTime);

    public string BestLapDisplay => FormatLapTime(BestLapTime);

    // The single fastest lap set by anyone in the session, across all cars — matches how RaceLab-
    // style overlays call out the session's benchmark lap.
    public string BestLapForeground => IsSessionFastestLap ? "#C060FF" : "#8FD3FF";

    public string IRatingDisplay => IRating > 0
        ? (IRating >= 1000 ? $"{(IRating / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)}k" : IRating.ToString(CultureInfo.InvariantCulture))
        : "—";

    public string LicStringDisplay => string.IsNullOrWhiteSpace(LicString) ? "—" : LicString;

    public string RowBackground => IsPlayer ? "#4433AAFF" : "Transparent";

    private static string FormatLapTime(double seconds) =>
        seconds > 0 ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture) : "—";
}
