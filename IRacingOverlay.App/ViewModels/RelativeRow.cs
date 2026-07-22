using System.Globalization;

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

    // InvariantCulture: this machine's locale uses a comma decimal separator, which silently turned
    // "+0.0" into "+0,0" in the live UI — a real display bug, not just a cosmetic preference.
    public string GapDisplay => IsPlayer
        ? "—"
        : (GapSeconds <= 0
            ? $"-{Math.Abs(GapSeconds).ToString("0.0", CultureInfo.InvariantCulture)}"
            : $"+{GapSeconds.ToString("0.0", CultureInfo.InvariantCulture)}");

    // Player keeps the brighter blue "find yourself" highlight; every other row is tinted by its
    // own class color so classes read apart at a glance without drowning the text. iRacing's class
    // colors ARE genuinely distinct hues (confirmed live: e.g. 0x33ceff vs 0xffda59) — the original
    // ~16% alpha ("#2A") was just too subtle against a near-black panel to let the hue read; both
    // ended up looking like similarly-dim gray. Bumped to ~33% ("#55") so the actual hue shows.
    public string RowBackground => IsPlayer ? "#4433AAFF" : $"#55{ClassColor.TrimStart('#')}";
}
