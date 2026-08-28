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
    public required double IRatingDelta { get; init; }
    public required bool IsSessionFastestLap { get; init; }
    public string ClassColor { get; init; } = "#FFFFFF";
    public int CarClassID { get; init; }
    public string CarClassName { get; init; } = "";

    // Formatted with InvariantCulture throughout: this machine's locale uses a comma decimal
    // separator, which silently turned "+0.0" into "+0,0" in the live UI — a real display bug.
    public string GapDisplay => Position == 1 ? "Leader" : $"+{GapToLeaderSeconds.ToString("0.0", CultureInfo.InvariantCulture)}";

    public string PositionDisplay => IsMultiClass ? $"{Position} ({ClassPosition})" : Position.ToString(CultureInfo.InvariantCulture);

    public string LastLapDisplay => FormatLapTime(LastLapTime);

    public string BestLapDisplay => FormatLapTime(BestLapTime);

    // The single fastest lap set by anyone in the session, across all cars — matches how RaceLab-
    // style overlays call out the session's benchmark lap.
    public string BestLapForeground => IsSessionFastestLap ? "#C88BFF" : "#8FD3FF";

    public string IRatingDisplay => IRating > 0
        ? (IRating >= 1000 ? $"{(IRating / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)}k" : IRating.ToString(CultureInfo.InvariantCulture))
        : "—";

    public string LicStringDisplay => string.IsNullOrWhiteSpace(LicString) ? "—" : LicString;

    // iRacing's own license-bar colors: Rookie red, D orange, C yellow, B green, A blue, Pro purple.
    // Falls back to gray for a blank/unrecognized license string rather than guessing.
    public string LicenseColor => (string.IsNullOrWhiteSpace(LicString) ? ' ' : char.ToUpperInvariant(LicString[0])) switch
    {
        'R' => "#E0413D",
        'D' => "#E08A2E",
        'C' => "#E0C93D",
        'B' => "#3DBF5C",
        'A' => "#3D7FE0",
        'P' => "#9B4DE0",
        _ => "#666666",
    };

    // Estimated points swing for the current race — StandingsBuilder's pairwise-duel approximation
    // of iRacing's undisclosed iRating formula. Direction and rough magnitude only; iRacing has
    // never published the exact constant, so this won't necessarily match the real post-race number.
    public string IRatingDeltaDisplay => IRating > 0
        ? (IRatingDelta >= 0 ? $"+{Math.Round(IRatingDelta):0}" : Math.Round(IRatingDelta).ToString(CultureInfo.InvariantCulture))
        : "—";

    public string IRatingDeltaForeground => IRatingDelta switch
    {
        > 0 => "#3DDC7A",
        < 0 => "#FF5A5A",
        _ => "#9BA5AE",
    };

    // Player keeps the brighter blue "find yourself" highlight; every other row is tinted by its
    // own class color so classes read apart at a glance without drowning the text. iRacing's class
    // colors ARE genuinely distinct hues (confirmed live: e.g. 0x33ceff vs 0xffda59) — the original
    // ~16% alpha ("#2A") was just too subtle against a near-black panel to let the hue read; both
    // ended up looking like similarly-dim gray. Bumped to ~33% ("#55") so the actual hue shows.
    public string RowBackground => IsPlayer ? "#4433AAFF" : $"#55{ClassColor.TrimStart('#')}";

    private static string FormatLapTime(double seconds) =>
        seconds > 0 ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture) : "—";
}
