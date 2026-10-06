namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// How much of a nearby car overlaps our own car's length, and *where* along that length — 0 at our
/// front bumper (top of the bar), 1 at our rear bumper (bottom). Overtaking someone sweeps
/// BandStart/BandEnd from a sliver at the top (we've just caught their rear bumper with our nose)
/// down to a sliver at the bottom (we're clearing their front) as we pass; being overtaken sweeps the
/// other way. A plain "amount alongside" scalar can't distinguish those two situations even though
/// they call for opposite reactions.
/// </summary>
public readonly record struct ProximitySide(double Amount, double BandStart, double BandEnd)
{
    public static ProximitySide None { get; } = new(0, 0, 0);
}

public sealed class CockpitState
{
    // 5 green + 5 yellow + 4 red, matching the reference dashboard design.
    public const int ShiftLightCount = 14;

    public required string Gear { get; init; }
    public required int ShiftLightsLit { get; init; }
    public required bool ShiftBlink { get; init; }
    public required bool AbsActive { get; init; }

    /// <summary>The car's configured ABS setting; null when the car has no adjustable ABS.</summary>
    public int? AbsLevel { get; init; }
    public required double SpeedKph { get; init; }
    public required double Rpm { get; init; }

    /// <summary>See CockpitBuilder for how this is approximated — iRacing doesn't expose other cars'
    /// lateral position at all.</summary>
    public required ProximitySide LeftProximity { get; init; }
    public required ProximitySide RightProximity { get; init; }

    // Readouts of the optional modules. Null when the car doesn't report them.
    public double? FuelLiters { get; init; }
    public double? FuelPct { get; init; }
    public double Throttle { get; init; }
    public double Brake { get; init; }
    public double? WaterTempC { get; init; }
    public double? OilTempC { get; init; }

    /// <summary>The in-car brake bias, front share in percent; null when the car has no adjuster.</summary>
    public double? BrakeBias { get; init; }

    /// <summary>The configured traction-control level; null when the car has no adjustable TC.</summary>
    public int? TractionControl { get; init; }

    /// <summary>The player's incidents and the limit, as the Incidents widget reads them; null when
    /// not reported.</summary>
    public IncidentState? Incidents { get; init; }

    /// <summary>The live delta to the session's best lap, as the Delta widget reads it; null when
    /// the sim doesn't report one.</summary>
    public DeltaState? Delta { get; init; }

    /// <summary>Modules this car can't feed, known from the variables the sim declares when the car
    /// loads — so it is fixed for the session and a module never comes and goes while driving. Empty
    /// unless built from live telemetry: the preview and a disconnected cockpit show every module.</summary>
    public IReadOnlySet<CockpitModule> Unsupported { get; init; } = NoneUnsupported;

    public UnitSystem UnitSystem { get; init; }

    private static readonly IReadOnlySet<CockpitModule> NoneUnsupported = new HashSet<CockpitModule>();

    public static CockpitState Empty { get; } = new()
    {
        Gear = "–",
        ShiftLightsLit = 0,
        ShiftBlink = false,
        AbsActive = false,
        SpeedKph = 0,
        Rpm = 0,
        LeftProximity = ProximitySide.None,
        RightProximity = ProximitySide.None,
    };
}
