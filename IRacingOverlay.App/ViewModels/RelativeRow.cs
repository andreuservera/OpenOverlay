using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

/// <summary>A line in the Relative table: everything <see cref="DriverRow"/> renders, with the gap
/// measured to the player rather than to a leader — negative ahead, positive behind.</summary>
public sealed class RelativeRow : DriverRow
{
    /// <summary>NaN when there is no clock to read it on (no estimated lap time for the player's car);
    /// the row still sits in its place on track.</summary>
    public required double GapSeconds { get; init; }

    // Formatted with InvariantCulture: this machine's locale uses a comma decimal separator, which
    // silently turned "+0.0" into "+0,0" in the live UI — a real display bug.
    // The player's own row reads "0.0" (muted by the row template); cars ahead take the typographic
    // minus (U+2212), which is as wide as the plus, so the column never shifts between the two.
    public override string GapDisplay => IsPlayer
        ? "0.0"
        : double.IsNaN(GapSeconds)
            ? "—"
            : (GapSeconds <= 0
                ? $"\u2212{Math.Abs(GapSeconds).ToString("0.0", CultureInfo.InvariantCulture)}"
                : $"+{GapSeconds.ToString("0.0", CultureInfo.InvariantCulture)}");

    /// <summary>Your own "0.0" is a reference point, not a reading: muted.</summary>
    public override string GapForeground => IsPlayer ? "#8B95A3" : base.GapForeground;
}

/// <summary>
/// A reserved, empty line. Relative's row count would otherwise breathe in and out constantly as
/// cars drift past the edges of the window, resizing the widget mid-corner; holding the configured
/// number of slots keeps its height fixed. Only used once the session actually has enough drivers to
/// fill them, so a small or solo session still renders compact rather than mostly blank.
/// </summary>
public sealed class RelativePlaceholderRow;
