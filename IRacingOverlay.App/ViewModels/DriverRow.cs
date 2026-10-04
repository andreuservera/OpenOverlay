using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

/// <summary>Where a car is in the race relative to the player, whatever their order on track.</summary>
public enum LapRelation
{
    SameLap,
    /// <summary>A lap or more down on the player: the player is lapping them.</summary>
    Lapped,
    /// <summary>A lap or more up on the player: they are lapping, or have lapped, the player.</summary>
    Lapping,
    /// <summary>A lapped car just up the road: the player is about to put it a(nother) lap down.</summary>
    BeingLapped,
}

/// <summary>A penalty flag a driver-table row can show.</summary>
public enum PenaltyFlag
{
    None,
    Black,
    Meatball,
    Furled,
}

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
    public bool HasBlackFlag { get; init; }
    /// <summary>The furled black flag: a warning, or a slow-down penalty to serve on track.</summary>
    public bool HasFurledFlag { get; init; }
    public bool HasMeatballFlag { get; init; }

    /// <summary>The row's tag holds two flags at most, the more serious first: black (serve a
    /// penalty in the pits), meatball (pit for repairs), furled black (slow down).</summary>
    public PenaltyFlag PrimaryPenalty => HasBlackFlag ? PenaltyFlag.Black
        : HasMeatballFlag ? PenaltyFlag.Meatball
        : HasFurledFlag ? PenaltyFlag.Furled
        : PenaltyFlag.None;

    public PenaltyFlag SecondaryPenalty => PrimaryPenalty switch
    {
        PenaltyFlag.Black when HasMeatballFlag => PenaltyFlag.Meatball,
        PenaltyFlag.Black or PenaltyFlag.Meatball when HasFurledFlag => PenaltyFlag.Furled,
        _ => PenaltyFlag.None,
    };

    public bool HasPenaltyFlag => PrimaryPenalty != PenaltyFlag.None;
    public required int CurrentLap { get; init; }
    public required double LastLapTime { get; init; }
    public required double BestLapTime { get; init; }
    public required bool IsMultiClass { get; init; }
    public required int IRating { get; init; }
    public required string LicString { get; init; }
    public required double IRatingDelta { get; init; }
    /// <summary>Fastest lap of the session within the driver's own class.</summary>
    public required bool IsSessionFastestLap { get; init; }
    public string ClassColor { get; init; } = "#FFFFFF";
    public int CarClassID { get; init; }
    public string CarClassName { get; init; } = "";

    /// <summary>Only Relative sets this; the row template tints lapped cars blue and lapping cars red.</summary>
    public LapRelation LapRelation { get; init; }

    /// <summary>Most recent completed pit stop this session; null until the car has made one.</summary>
    public PitStop? LastPitStop { get; init; }

    /// <summary>The tyre the car is on; null when iRacing doesn't say.</summary>
    public TireCompound? TireCompound { get; init; }

    public bool HasTireCompound => TireCompound is not null;

    public string TireCompoundLetter => TireCompound?.Letter ?? "";

    public string TireCompoundColor => TireCompound?.Color ?? "#8E99A5";

    /// <summary>The car's make, or the placeholder for one the app doesn't know.</summary>
    public CarBrand? CarBrand { get; init; }

    public string CarBrandLogo => CarBrand?.Logo ?? "";

    public string CarBrandMonogram => CarBrand?.Monogram ?? "";

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

    public bool HasLastPitStop => LastPitStop is not null;

    public string LastPitLapDisplay => LastPitStop is { } stop ? $"L{stop.Lap.ToString(CultureInfo.InvariantCulture)}" : "";

    public string LastPitDurationDisplay => LastPitStop is { } stop
        ? FormatMinutesSeconds(stop.Seconds)
        : "";

    private static string FormatMinutesSeconds(double seconds)
    {
        var total = (int)Math.Round(Math.Max(0, seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{total / 60:00}:{total % 60:00}");
    }

    public string BestLapDisplay => FormatLapTime(BestLapTime);

    // Purple is reserved for the fastest lap of each class — the one lap time worth colouring.
    // Every other best lap is just secondary data.
    public string BestLapForeground => IsSessionFastestLap ? "#B58CFF" : "#C4CCD4";

    // Broadcast convention: green when the lap just completed is the driver's personal best, purple
    // when that personal best is also the fastest lap of their class.
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
    /// Abbreviates from the front once a name is too long for the driver column: "Maria Garcia
    /// Lopez" becomes "M. G. Lopez". The name keeps the case the driver registered it in. The surname stays intact because that's what identifies the
    /// driver on a timing screen, and every row keeps ending in a real word instead of a row of
    /// ellipses that all look alike at speed. A single long token is left for the UI to trim, since
    /// there's nothing meaningful to drop.
    /// </summary>
    private static string ShortenName(string raw)
    {
        var name = (raw ?? string.Empty).Trim();
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
