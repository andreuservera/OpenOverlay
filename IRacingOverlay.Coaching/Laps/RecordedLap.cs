namespace IRacingOverlay.Coaching.Laps;

/// <summary>Why a lap doesn't count as a clean, representative lap.</summary>
[Flags]
public enum LapFlaw
{
    None = 0,

    /// <summary>Recording began part-way round: the out lap, or the lap the coach was attached in.</summary>
    Partial = 1 << 0,

    PitRoad = 1 << 1,

    /// <summary>All four wheels off (PlayerTrackSurface = OffTrack).</summary>
    OffTrack = 1 << 2,

    /// <summary>PlayerCarMyIncidentCount went up during the lap.</summary>
    Incident = 1 << 3,

    /// <summary>Get in car, warmup, parade laps or cool down rather than driving at speed.</summary>
    NotRacing = 1 << 4,

    /// <summary>The car left the world (tow, reset, exit to garage).</summary>
    NotOnTrack = 1 << 5,

    /// <summary>A stretch with no telemetry (paused, alt-tabbed sim, file gap).</summary>
    TelemetryGap = 1 << 6,

    /// <summary>The car went backwards or was moved (reset to track).</summary>
    Discontinuity = 1 << 7,

    /// <summary>The first lap after time spent outside racing (grid, formation lap, pit stall):
    /// a race start or out lap.</summary>
    StartLap = 1 << 8,
}

/// <summary>One completed lap as the recorder saw it.</summary>
public sealed class RecordedLap
{
    /// <summary>iRacing's Lap counter during this lap.</summary>
    public required int LapNumber { get; init; }

    public required int SessionNum { get; init; }

    /// <summary>Session time at the line crossing that started the lap, interpolated between ticks.</summary>
    public required double StartTime { get; init; }

    /// <summary>Line to line, interpolated between ticks, seconds.</summary>
    public required double LapTime { get; init; }

    /// <summary>iRacing's own time for this lap (LapLastLapTime shortly after the line), when it reported one.</summary>
    public double? ReportedLapTime { get; set; }

    public required LapFlaw Flaws { get; init; }

    public bool IsClean => Flaws == LapFlaw.None;

    /// <summary>The lap on a 1 m grid. Null for partial laps, which don't cover the whole track.</summary>
    public LapTrace? Trace { get; init; }
}
