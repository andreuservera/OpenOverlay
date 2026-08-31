using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// One driver's line in a timing table, with every column's formatting. Shared by Standings and
/// Relative so the two tables can't drift apart: they render from the same template against the
/// same column set, and the only thing each defines for itself is what its GAP column means.
/// </summary>
public abstract class DriverRow
{
    // Roughly what the fixed driver column fits at the base type size. Past this the name is
    // abbreviated rather than left to the ellipsis.
    private const int NameBudget = 16;

    public required int CarIdx { get; init; }
    public required int Position { get; init; }
    public required int ClassPosition { get; init; }
    public required string Name { get; init; }
    public required string CarNumber { get; init; }
    public required bool IsPlayer { get; init; }
    public required bool OnPitRoad { get; init; }
    public required int CurrentLap { get; init; }
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

    /// <summary>What the GAP column shows. The one thing the two tables genuinely disagree on:
    /// Standings measures to the class leader, Relative to the player.</summary>
    public abstract string GapDisplay { get; }

    /// <summary>The position that actually means something to the driver. In multiclass that's the
    /// standing within their own class — overall position mixes categories that never race each
    /// other, so a GT3 leader reading "20" tells them nothing. Single-class sessions have only one
    /// position, and the two are the same number.</summary>
    public string PositionDisplay => RankInOwnRace.ToString(CultureInfo.InvariantCulture);

    protected int RankInOwnRace => IsMultiClass ? ClassPosition : Position;

    public string CarNumberDisplay => string.IsNullOrWhiteSpace(CarNumber) ? "—" : CarNumber;

    /// <summary>iRacing reports -1 for a car that isn't on track and has nothing scored, which is
    /// "no laps", not minus one lap. Shown as a dash like every other unknown value.</summary>
    public string LapDisplay => CurrentLap >= 0 ? CurrentLap.ToString(CultureInfo.InvariantCulture) : "—";

    /// <summary>Always upper case, and short enough to fit the fixed driver column without the
    /// ellipsis doing the work. See <see cref="ShortenName"/>.</summary>
    public string NameDisplay => ShortenName(Name);

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
    // A swing that rounds to nothing reads as a dash, not "+0": practice and qualifying produce no
    // estimate at all, and "+0" there looks like a computed result rather than an absent one.
    public string IRatingDeltaDisplay => HasIRatingDelta
        ? (RoundedIRatingDelta > 0 ? $"+{RoundedIRatingDelta:0}" : RoundedIRatingDelta.ToString("0", CultureInfo.InvariantCulture))
        : "—";

    public string IRatingDeltaForeground => !HasIRatingDelta
        ? "#D2D8DE"
        : RoundedIRatingDelta > 0
            ? "#3DDC7A"
            // Lighter than the app's standard critical red: measured on a row background, a
            // saturated red carries too little luminance to clear 4.5:1 — it needed lifting this far
            // to stay readable on the player's own row and on a bright class colour. Still
            // unmistakably the warm half of the pair against the green above it.
            : "#FFB3B3";

    // Rounded before the zero test, so a swing of 0.4 shows a dash rather than "+0".
    private double RoundedIRatingDelta => Math.Round(IRatingDelta);

    private bool HasIRatingDelta => IRating > 0 && RoundedIRatingDelta != 0;

    public string RowBackground => RowTint.For(IsPlayer, OnPitRoad, IsMultiClass, ClassColor);

    /// <summary>The leader of the race — or of the class, in multiclass — gets the accent colour on
    /// their position number. Marking them there rather than with a fourth row background keeps the
    /// table calm while still calling them out.</summary>
    public string PositionForeground => RankInOwnRace == 1 ? "#FFD24D" : "#FFFFFF";

    /// <summary>Solid bar down the left edge of the row. Only the player gets one, so "where am I"
    /// survives a glance too quick to read a background tint against a bright track.</summary>
    public string RowAccent => IsPlayer ? "#33AAFF" : "#00000000";

    /// <summary>
    /// Abbreviates from the front once a name is too long for the driver column: "MARIA GARCIA
    /// LOPEZ" becomes "M. G. LOPEZ". The surname stays intact because that's what identifies the
    /// driver on a timing screen, and every row keeps ending in a real word instead of a row of
    /// ellipses that all look alike at speed. A single long token is left for the UI to trim, since
    /// there's nothing meaningful to drop.
    /// </summary>
    private static string ShortenName(string raw)
    {
        var name = (raw ?? string.Empty).Trim().ToUpperInvariant();
        if (name.Length <= NameBudget)
        {
            return name;
        }

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return name;
        }

        var shortened = string.Concat(parts[..^1].Select(p => $"{p[0]}. ")) + parts[^1];
        return shortened.Length < name.Length ? shortened : name;
    }

    private static string FormatLapTime(double seconds) =>
        seconds > 0 ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture) : "—";
}
