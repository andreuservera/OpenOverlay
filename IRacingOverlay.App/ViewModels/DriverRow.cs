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

    // Purple is reserved for the single fastest lap of the session, across all cars — the one lap
    // time worth colouring. Every other best lap is just secondary data.
    public string BestLapForeground => IsSessionFastestLap ? "#B58CFF" : "#C4CCD4";

    // Broadcast convention: green when the lap just completed is the driver's personal best, purple
    // when that personal best is also the fastest lap of the session.
    public string LastLapForeground => !IsLastLapPersonalBest
        ? "#C4CCD4"
        : IsSessionFastestLap ? "#B58CFF" : "#34D399";

    private bool IsLastLapPersonalBest =>
        LastLapTime > 0 && BestLapTime > 0 && Math.Abs(LastLapTime - BestLapTime) < 0.0005;

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
    // A swing that rounds to nothing reads as a dash, not "0": practice and qualifying produce no
    // estimate at all, and "0" there looks like a computed result rather than an absent one.
    // The magnitude only: the direction is drawn as an arrow (see IRatingTrend).
    public string IRatingDeltaDisplay => HasIRatingDelta
        ? Math.Abs(RoundedIRatingDelta).ToString("0", CultureInfo.InvariantCulture)
        : "—";

    /// <summary>+1 gaining, -1 losing, 0 no estimate — picks the arrow in the iRating badge.</summary>
    public int IRatingTrend => HasIRatingDelta ? Math.Sign(RoundedIRatingDelta) : 0;

    public string IRatingDeltaForeground => !HasIRatingDelta
        ? "#8E99A5"
        : RoundedIRatingDelta > 0 ? "#34D399" : "#FF6B6B";

    // Rounded before the zero test, so a swing of 0.4 shows a dash rather than "+0".
    private double RoundedIRatingDelta => Math.Round(IRatingDelta);

    private bool HasIRatingDelta => IRating > 0 && RoundedIRatingDelta != 0;

    /// <summary>The leader of the race — or of the class, in multiclass — gets the accent colour on
    /// their position number. Marking them there rather than with another row background keeps the
    /// table calm while still calling them out. Row states (player, pits, class) are drawn by the
    /// row template in Themes/DriverTable.xaml.</summary>
    public string PositionForeground => RankInOwnRace == 1 ? "#FFD24D" : "#F2F5F8";

    /// <summary>The gap is the number a driver reads the table for, so it gets primary ink.</summary>
    public virtual string GapForeground => "#F2F5F8";

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
