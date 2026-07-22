using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

public sealed class FuelState
{
    public required double LevelLiters { get; init; }
    public required double LevelPct { get; init; }
    /// <summary>Average liters burned per lap, computed from the fuel level drop across every
    /// completed lap this session (not iRacing's instantaneous burn rate, which jumps around lap to
    /// lap). 0 when it can't be estimated yet (e.g. no lap completed).</summary>
    public required double PerLapLiters { get; init; }
    /// <summary>0 when PerLapLiters is 0 (can't estimate).</summary>
    public required double LapsOfFuelRemaining { get; init; }
    /// <summary>null when the session has no lap limit (timed or open session).</summary>
    public required int? LapsRemainingInSession { get; init; }

    public bool HasEstimate => PerLapLiters > 0;

    /// <summary>null when either side of the comparison isn't known yet.</summary>
    public bool? WillMakeItToTheEnd => HasEstimate && LapsRemainingInSession is { } laps
        ? LapsOfFuelRemaining >= laps
        : null;

    public string LevelDisplay => LevelLiters > 0 ? LevelLiters.ToString("0.0", CultureInfo.InvariantCulture) + " L" : "—";
    public string LapsOfFuelDisplay => HasEstimate ? LapsOfFuelRemaining.ToString("0.0", CultureInfo.InvariantCulture) : "—";
    public string PerLapDisplay => HasEstimate ? PerLapLiters.ToString("0.00", CultureInfo.InvariantCulture) + " L/lap" : "—";

    public static FuelState Empty { get; } = new()
    {
        LevelLiters = 0,
        LevelPct = 0,
        PerLapLiters = 0,
        LapsOfFuelRemaining = 0,
        LapsRemainingInSession = null,
    };
}
