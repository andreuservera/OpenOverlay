using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Everything the Fuel Calculator widget renders for one tick. Deliberately limited to figures a
/// driver can act on mid-race — level, per-lap usage, how far the tank goes, and whether that
/// reaches the flag — with no lap tables or history graphs.
/// </summary>
public sealed class FuelCalculatorState
{
    public required double LevelLiters { get; init; }
    public required double LevelPct { get; init; }

    /// <summary>Fuel burned on the most recently completed lap. 0 until one lap is in the books.</summary>
    public required double LastLapLiters { get; init; }

    /// <summary>Average burn over whichever window the user picked. 0 until one lap is in the books.</summary>
    public required double AverageLiters { get; init; }

    /// <summary>Best/worst completed lap this session — always whole-session regardless of the
    /// average window, since "my most economical lap ever" is the useful reading, not "within the
    /// last 3."</summary>
    public required double MinLiters { get; init; }
    public required double MaxLiters { get; init; }

    /// <summary>Laps the current tank covers at the selected average. 0 when there's no estimate yet.</summary>
    public required double LapsRemainingWithFuel { get; init; }

    /// <summary>Laps left in the session, from the lap counter or (in a timed session) the clock
    /// divided by lap time. Null when the session has no defined end, e.g. open practice.</summary>
    public required double? LapsLeftInSession { get; init; }

    /// <summary>Total fuel needed to reach the flag including the configured safety margin. 0 when
    /// it can't be computed.</summary>
    public required double FuelToFinishLiters { get; init; }

    /// <summary>Current level minus what's needed: positive = surplus, negative = shortfall.</summary>
    public required double FuelDeltaLiters { get; init; }

    /// <summary>Usable tank capacity in liters (physical size times any series fuel restriction).
    /// 0 when the session info hasn't reported it yet.</summary>
    public required double TankCapacityLiters { get; init; }

    public bool HasUsageEstimate => AverageLiters > 0;

    /// <summary>Gates every "to the finish" figure. Without a known session end there is no finish
    /// to compute against, so those blocks stay hidden instead of showing a made-up number —
    /// iRacing exposes no reliable "is refueling allowed" flag to key off instead.</summary>
    public bool IsSessionEndKnown => LapsLeftInSession is not null;

    public bool CanProjectToFinish => IsSessionEndKnown && HasUsageEstimate;

    public bool HasTankCapacity => TankCapacityLiters > 0;

    /// <summary>Drives the warning color — only a genuine "you will run dry before the flag", never
    /// the fill-to-max suggestion below, which is routine rather than a problem.</summary>
    public bool IsShortOfFuel => CanProjectToFinish && FuelDeltaLiters < 0;

    /// <summary>
    /// Liters to add. With a known finish that's the shortfall; without one (test drive, open
    /// practice) there's no target to compute against, so the recommendation falls back to simply
    /// topping the tank up to its maximum. Rounded up to a whole liter: iRacing's own pit black box
    /// takes whole liters, and rounding down would leave you exactly short of the number just
    /// calculated.
    /// </summary>
    public double RefuelLiters => CanProjectToFinish
        ? (FuelDeltaLiters < 0 ? Math.Ceiling(-FuelDeltaLiters) : 0)
        : HasTankCapacity ? Math.Max(0, Math.Ceiling(TankCapacityLiters - LevelLiters)) : 0;

    public bool NeedsRefuel => RefuelLiters > 0;

    public string LevelDisplay => LevelLiters > 0 ? Format(LevelLiters, "0.0") : "—";
    public string LastLapDisplay => LastLapLiters > 0 ? Format(LastLapLiters, "0.00") : "—";
    public string AverageDisplay => AverageLiters > 0 ? Format(AverageLiters, "0.00") : "—";
    public string MinDisplay => MinLiters > 0 ? Format(MinLiters, "0.00") : "—";
    public string MaxDisplay => MaxLiters > 0 ? Format(MaxLiters, "0.00") : "—";
    public string LapsRemainingDisplay => HasUsageEstimate ? Format(LapsRemainingWithFuel, "0.0") : "—";

    /// <summary>Signed so the sign itself carries the meaning at a glance: "+2.4 L" spare versus
    /// "-3.1 L" short.</summary>
    public string FuelDeltaDisplay => CanProjectToFinish
        ? (FuelDeltaLiters >= 0 ? "+" : "-") + Format(Math.Abs(FuelDeltaLiters), "0.0") + " L"
        : "—";

    public string FuelToFinishDisplay => CanProjectToFinish ? Format(FuelToFinishLiters, "0.0") + " L" : "—";

    // "TO FULL" rather than a bare number when there's no finish to aim at, so the figure can't be
    // mistaken for a computed strategy call.
    public string RefuelDisplay => CanProjectToFinish
        ? (NeedsRefuel ? Format(RefuelLiters, "0") + " L" : "NOT NEEDED")
        : HasTankCapacity
            ? (NeedsRefuel ? Format(RefuelLiters, "0") + " L TO FULL" : "FULL")
            : "—";

    // InvariantCulture throughout: a comma decimal separator has already caused a real display bug
    // in this codebase (see RelativeRow.GapDisplay).
    private static string Format(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    public static FuelCalculatorState Empty { get; } = new()
    {
        LevelLiters = 0,
        LevelPct = 0,
        LastLapLiters = 0,
        AverageLiters = 0,
        MinLiters = 0,
        MaxLiters = 0,
        LapsRemainingWithFuel = 0,
        LapsLeftInSession = null,
        FuelToFinishLiters = 0,
        FuelDeltaLiters = 0,
        TankCapacityLiters = 0,
    };
}
