using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

/// <summary>A line in the Standings table: everything <see cref="DriverRow"/> renders, with the gap
/// measured to the leader of this driver's own class.</summary>
public sealed class StandingsRow : DriverRow
{
    public required double GapToLeaderSeconds { get; init; }

    // Formatted with InvariantCulture: this machine's locale uses a comma decimal separator, which
    // silently turned "+0.0" into "+0,0" in the live UI — a real display bug.
    public override string GapDisplay => RankInOwnRace == 1
        ? "Leader"
        : $"+{GapToLeaderSeconds.ToString("0.0", CultureInfo.InvariantCulture)}";
}
