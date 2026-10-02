using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

/// <summary>A line in the Standings table: everything <see cref="DriverRow"/> renders, with the gap
/// measured to the leader of this driver's own class.</summary>
public sealed class StandingsRow : DriverRow
{
    /// <summary>NaN until the gap has been measured at the line; the order still stands.</summary>
    public required double GapToLeaderSeconds { get; init; }

    /// <summary>Whole laps behind the class leader when last at the line. Shown instead of seconds.</summary>
    public int LapsDown { get; init; }

    // Formatted with InvariantCulture: this machine's locale uses a comma decimal separator, which
    // silently turned "+0.0" into "+0,0" in the live UI — a real display bug.
    public override string GapDisplay => RankInOwnRace == 1
        ? "Leader"
        : LapsDown > 0
            ? string.Create(CultureInfo.InvariantCulture, $"+{LapsDown}L")
            : double.IsNaN(GapToLeaderSeconds)
                ? "—"
                : $"+{GapToLeaderSeconds.ToString("0.0", CultureInfo.InvariantCulture)}";

    // "Leader" takes the same accent as the leader's position number.
    public override string GapForeground => RankInOwnRace == 1 ? "#FFD24D" : base.GapForeground;
}
