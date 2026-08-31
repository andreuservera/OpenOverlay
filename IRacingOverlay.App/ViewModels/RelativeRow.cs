using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

/// <summary>A line in the Relative table: everything <see cref="DriverRow"/> renders, with the gap
/// measured to the player rather than to a leader — negative ahead, positive behind.</summary>
public sealed class RelativeRow : DriverRow
{
    public required double GapSeconds { get; init; }

    // Formatted with InvariantCulture: this machine's locale uses a comma decimal separator, which
    // silently turned "+0.0" into "+0,0" in the live UI — a real display bug.
    public override string GapDisplay => IsPlayer
        ? "—"
        : (GapSeconds <= 0
            ? $"-{Math.Abs(GapSeconds).ToString("0.0", CultureInfo.InvariantCulture)}"
            : $"+{GapSeconds.ToString("0.0", CultureInfo.InvariantCulture)}");
}

/// <summary>
/// A reserved, empty line. Relative's row count would otherwise breathe in and out constantly as
/// cars drift past the edges of the window, resizing the widget mid-corner; holding the configured
/// number of slots keeps its height fixed. Only used once the session actually has enough drivers to
/// fill them, so a small or solo session still renders compact rather than mostly blank.
/// </summary>
public sealed class RelativePlaceholderRow;
