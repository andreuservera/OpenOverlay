using IRacingOverlay.Coaching.Corners;

namespace IRacingOverlay.Coaching.Advice;

/// <summary>How the live coach gives its cue before a focus corner.</summary>
public enum CueMode
{
    Off,

    /// <summary>A short notice on screen, nothing spoken.</summary>
    Notice,

    /// <summary>Spoken, two or three words.</summary>
    Voice,
}

/// <summary>A corner to cue before, and what to say.</summary>
/// <param name="Kind">The recurring mistake there (a <see cref="FindingKind"/> name).</param>
public sealed record FocusCue(int ApexM, string CornerName, string Kind)
{
    /// <summary>Two or three words to hear just before the corner ("brake later").</summary>
    public string Words => CornerCues.Words(Kind) ?? "focus";
}

/// <summary>
/// Phase 3 of the coach: one cue of two or three words just before each focus corner, only while
/// live coaching is on. The cue comes once a lap, about three seconds before the corner, and goes
/// away for good once the mistake hasn't come back for <see cref="FixedAfterLaps"/> laps running.
/// Not thread-safe: feed it from the telemetry thread.
/// </summary>
public sealed class CornerCues
{
    /// <summary>Laps in a row without the mistake before its cue stops.</summary>
    public const int FixedAfterLaps = 3;

    /// <summary>The cue comes this long before the corner starts, seconds…</summary>
    public const double LeadSeconds = 3;

    /// <summary>…but never closer than this or further than that, metres.</summary>
    public const int MinLeadMetres = 120, MaxLeadMetres = 400;

    private readonly List<State> _cues;

    public CornerCues(IEnumerable<FocusCue> cues)
    {
        _cues = cues.Where(c => Words(c.Kind) is not null).Select(c => new State(c)).ToList();
    }

    /// <summary>A cue to give now.</summary>
    public event Action<FocusCue>? Cue;

    /// <summary>A focus corner whose mistake has stayed away for <see cref="FixedAfterLaps"/> laps.</summary>
    public event Action<FocusCue>? Fixed;

    public bool Active => _cues.Any(c => !c.Retired);

    /// <summary>Where the car is: called on every telemetry tick with a corner map.</summary>
    public void Observe(CornerMap map, int lap, double lapDistM, double speed, bool onPitRoad)
    {
        if (onPitRoad)
        {
            return;
        }

        var lead = Math.Clamp(speed * LeadSeconds, MinLeadMetres, MaxLeadMetres);
        foreach (var c in _cues)
        {
            if (c.Retired || c.CuedOnLap == lap)
            {
                continue;
            }

            var corner = map.Corners.Where(k => Math.Abs(k.ApexM - c.Cue.ApexM) <= CornerMapBuilder.ClusterRadiusMetres).MinBy(k => Math.Abs(k.ApexM - c.Cue.ApexM));
            if (corner is null)
            {
                continue;
            }

            // Ahead of the corner by up to the lead, wrapping past the line.
            var ahead = corner.StartM - lapDistM;
            if (ahead < 0)
            {
                ahead += map.TrackLengthMetres;
            }

            if (ahead > 0 && ahead <= lead)
            {
                c.CuedOnLap = lap;
                Cue?.Invoke(c.Cue);
            }
        }
    }

    /// <summary>After each clean lap's debrief: whether the focus corners' mistake came back.</summary>
    public void OnDebrief(LapDebrief d)
    {
        foreach (var c in _cues.Where(c => !c.Retired))
        {
            var advice = d.Corners.FirstOrDefault(a => Math.Abs(a.Corner.ApexM - c.Cue.ApexM) <= CornerMapBuilder.ClusterRadiusMetres);
            var again = advice?.Main is { } m && m.Kind.ToString() == c.Cue.Kind;
            c.CleanLaps = again ? 0 : c.CleanLaps + 1;
            if (c.CleanLaps >= FixedAfterLaps)
            {
                c.Retired = true;
                Fixed?.Invoke(c.Cue);
            }
        }
    }

    /// <summary>The words for a mistake, short enough to take in before a corner; null for none.</summary>
    public static string? Words(string kind) => kind switch
    {
        nameof(FindingKind.BrakedTooEarly) => "brake later",
        nameof(FindingKind.BrakedTooLate) => "brake earlier",
        nameof(FindingKind.InitialBrakeTooSoft) => "brake harder",
        nameof(FindingKind.InitialBrakeTooHard) => "softer brake",
        nameof(FindingKind.TooLittleTrailBrake) => "trail the brake",
        nameof(FindingKind.LateThrottle) => "throttle earlier",
        nameof(FindingKind.LowMinimumSpeed) => "carry speed",
        nameof(FindingKind.SlowExit) => "exit speed",
        nameof(FindingKind.LiftedInFlatCorner) => "stay flat",
        nameof(FindingKind.LiftedTooEarly) => "lift later",
        nameof(FindingKind.LiftTooDeep) => "smaller lift",
        nameof(FindingKind.LiftTooLong) => "shorter lift",
        nameof(FindingKind.UnneededBrake) => "no brake",
        nameof(FindingKind.Coasting) => "no coasting",
        nameof(FindingKind.ThrottleHesitation) => "smooth throttle",
        nameof(FindingKind.Understeer) => "ease off the brake",
        _ => null,
    };

    private sealed class State(FocusCue cue)
    {
        public FocusCue Cue { get; } = cue;

        public int CuedOnLap { get; set; } = -1;

        public int CleanLaps { get; set; }

        public bool Retired { get; set; }
    }
}
