namespace IRacingOverlay.Coaching.Reports;

/// <summary>
/// What a coached session leaves behind for the post-session page: the laps, the best lap and the
/// theoretical best, every corner measured across all the laps, and the mistakes that came back lap
/// after lap. Plain data, saved as JSON (see <see cref="SessionReportStore"/>), so the app can list
/// and show sessions without the telemetry. Built by <see cref="SessionReportBuilder"/>.
///
/// Finding kinds are kept as their names rather than as an enum, so a report written by a newer
/// coach (with kinds this one doesn't know) still opens.
/// </summary>
public sealed record SessionReport
{
    /// <summary>Bumped when the shape changes in a way older readers can't take.</summary>
    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;

    /// <summary>Also the file name: unique per session.</summary>
    public required string Id { get; init; }

    /// <summary>"live" (coached while driving) or "ibt" (analysed from a telemetry file).</summary>
    public required string Source { get; init; }

    /// <summary>The .ibt file it came from, for <see cref="Source"/> "ibt".</summary>
    public string? SourceFile { get; init; }

    public DateTime StartedUtc { get; init; }

    public DateTime UpdatedUtc { get; init; }

    /// <summary>iRacing's internal track name (WeekendInfo.TrackName).</summary>
    public string? TrackKey { get; init; }

    public string? TrackName { get; init; }

    public string? CarName { get; init; }

    /// <summary>Practice, Qualify, Race… when known.</summary>
    public string? SessionType { get; init; }

    public int TrackLengthMetres { get; init; }

    /// <summary>Every lap recorded, in order, clean or not.</summary>
    public List<LapRow> Laps { get; init; } = [];

    public int? BestLapNumber { get; init; }

    public double? BestLapTime { get; init; }

    /// <summary>The best pass of every corner chained into one lap; null until every corner has a clean pass.</summary>
    public double? TheoreticalBestLapTime { get; init; }

    /// <summary>The incident lap after which the car was slower everywhere (likely damage); bests
    /// and comparisons only use the laps after it.</summary>
    public int? CarChangedAfterLap { get; init; }

    /// <summary>How far the clean laps were from the best lap, on average, seconds.</summary>
    public double? AverageGapToBest { get; init; }

    /// <summary>Spread (standard deviation) of the clean lap times, seconds: lower is more consistent.</summary>
    public double? CleanLapSpread { get; init; }

    /// <summary>The corners in lap order, each measured over every lap.</summary>
    public List<CornerReport> Corners { get; init; } = [];

    /// <summary>The mistakes that came back lap after lap, the costliest first.</summary>
    public List<RecurringMistake> Mistakes { get; init; } = [];

    /// <summary>Short sentences about the session as a whole (car damage, too few laps…).</summary>
    public List<string> Notes { get; init; } = [];

    public int CleanLapCount => Laps.Count(l => l.Clean);
}

/// <summary>One lap of the session.</summary>
public sealed record LapRow
{
    public int Number { get; init; }

    public double Time { get; init; }

    public bool Clean { get; init; }

    /// <summary>Why it didn't count (OffTrack, Incident, PitRoad…); empty when clean.</summary>
    public List<string> Flaws { get; init; } = [];

    /// <summary>Lap time minus the best lap, for clean laps.</summary>
    public double? DeltaToBest { get; init; }

    /// <summary>Time lost in its clean corners against the best pass of each, seconds.</summary>
    public double? CornerLoss { get; init; }

    /// <summary>The pieces of the lap that couldn't be used, with why ("T5: OffTrack").</summary>
    public List<string> UncleanPieces { get; init; } = [];

    /// <summary>False for laps before a car change: they aren't compared with the later bests.</summary>
    public bool Compared { get; init; } = true;
}

/// <summary>One corner measured across the whole session.</summary>
public sealed record CornerReport
{
    /// <summary>Stable id: T1, T2…</summary>
    public required string Id { get; init; }

    /// <summary>Real name when known, else the id.</summary>
    public required string Name { get; init; }

    /// <summary>"slow right", "left hairpin"…; null when it can't tell.</summary>
    public string? Kind { get; init; }

    /// <summary>Which way it turns on the best pass: +1 left, -1 right, 0 unknown (with the best
    /// pass's minimum speed, what <see cref="Kind"/> is worded from).</summary>
    public int Direction { get; init; }

    /// <summary>What decides its time: a <see cref="Corners.CornerType"/> name (Flat, Lift, BrakingFast,
    /// Slow, Complex); null when unknown.</summary>
    public string? Type { get; init; }

    public int StartM { get; init; }

    public int ApexM { get; init; }

    public int EndM { get; init; }

    /// <summary>Clean passes compared (laps after a car change only, when there was one).</summary>
    public int CleanPasses { get; init; }

    public int? BestLap { get; init; }

    /// <summary>Time of the best pass, corner plus the straight after it, seconds.</summary>
    public double? BestTime { get; init; }

    /// <summary>Median time lost here against the best pass, over the clean passes: what a normal
    /// lap gives away in this corner.</summary>
    public double? MedianLoss { get; init; }

    /// <summary>What the best lap lost here against the best pass: where the theoretical best gains.</summary>
    public double? BestLapLoss { get; init; }

    /// <summary>The best pass.</summary>
    public PassRow? Best { get; init; }

    /// <summary>A typical pass: the clean pass whose loss is the median.</summary>
    public PassRow? Typical { get; init; }

    /// <summary>Every pass, lap order.</summary>
    public List<PassRow> Passes { get; init; } = [];

    /// <summary>How often each first finding came up here, most frequent first.</summary>
    public List<FindingTally> Findings { get; init; } = [];

    /// <summary>Speed and pedals through the corner on the best pass.</summary>
    public CornerTrace? BestTrace { get; init; }

    /// <summary>Speed and pedals through the corner on the typical pass.</summary>
    public CornerTrace? TypicalTrace { get; init; }

    /// <summary>Where a normal lap loses its time here: the median loss against the best pass in
    /// each phase (approach, braking, rotation, traction, straight), seconds.</summary>
    public List<PhaseLossRow> LossByPhase { get; init; } = [];

    /// <summary>Share of the car's grip used under braking and from release to throttle, 0–1, on the
    /// best and on the typical pass; null when it can't be told.</summary>
    public GripUse? Grip { get; init; }

    /// <summary>How much the corner's time varies between clean passes (standard deviation), seconds.</summary>
    public double? TimeSpread { get; init; }

    /// <summary>How much the brake (or lift) point moves between clean passes (standard deviation), metres.</summary>
    public double? BrakePointSpreadM { get; init; }

    /// <summary>The measurement that best explains the time here across the passes, in words
    /// ("Minimum speed: higher was quicker"); null when nothing stands out.</summary>
    public string? DecidedBy { get; init; }
}

/// <summary>Median time lost in one phase of a corner, seconds.</summary>
public sealed record PhaseLossRow
{
    public required string Phase { get; init; }

    public double Seconds { get; init; }
}

/// <summary>Share of the car's grip used in a corner (see <c>GripEnvelope</c>), 0–1.</summary>
public sealed record GripUse
{
    public double? BestBraking { get; init; }

    public double? BestCornering { get; init; }

    public double? TypicalBraking { get; init; }

    public double? TypicalCornering { get; init; }
}

/// <summary>One lap's pass through a corner.</summary>
public sealed record PassRow
{
    public int Lap { get; init; }

    /// <summary>Corner plus the straight after it, seconds.</summary>
    public double Time { get; init; }

    /// <summary>Against the best pass; null when not clean or not compared.</summary>
    public double? Loss { get; init; }

    public bool Clean { get; init; }

    /// <summary>Why it isn't clean (OffTrack, Shortcut…); null when clean.</summary>
    public string? Flaws { get; init; }

    public bool IsBest { get; init; }

    public double EntryKmh { get; init; }

    public int? BrakePointM { get; init; }

    /// <summary>Peak brake pressure, % of the pedal.</summary>
    public double PeakBrakePct { get; init; }

    public int TrailBrakeM { get; init; }

    public double MinSpeedKmh { get; init; }

    public int MinSpeedAtM { get; init; }

    public int? ThrottleM { get; init; }

    /// <summary>Speed 100 m after the corner, km/h.</summary>
    public double ExitKmh { get; init; }

    public int Gear { get; init; }

    /// <summary>What differed from the best pass, as advice, cause first.</summary>
    public List<string> Findings { get; init; } = [];

    /// <summary>The same findings as data, for showing them in another language.</summary>
    public List<PassIssue> Issues { get; init; } = [];
}

/// <summary>One finding of a pass: its kind (a <see cref="Advice.FindingKind"/> name), size in the
/// kind's unit, and for a soft initial brake whether it was the ramp rather than the pressure.</summary>
public sealed record PassIssue(string Kind, double Amount, bool IsRamp = false)
{
    /// <summary>For coasting: the corner needs no brake, so the fix is to keep some throttle on.</summary>
    public bool NoBraking { get; init; }
}

/// <summary>How often one finding was the main thing wrong in a corner.</summary>
public sealed record FindingTally
{
    public required string Kind { get; init; }

    /// <summary>Laps it was the main finding on (and the corner lost real time).</summary>
    public int Laps { get; init; }

    /// <summary>Passes it could have come up on: the clean passes other than the best.</summary>
    public int OutOf { get; init; }

    /// <summary>Median size, in the finding's unit (metres, km/h, % of pedal).</summary>
    public double TypicalAmount { get; init; }

    /// <summary>For a soft initial brake: the pressure was slow to build rather than too low.</summary>
    public bool IsRamp { get; init; }

    /// <summary>For coasting: the corner needs no brake.</summary>
    public bool NoBraking { get; init; }

    /// <summary>Median time lost on those laps, seconds.</summary>
    public double TypicalLoss { get; init; }

    /// <summary>Time lost on those laps together, seconds.</summary>
    public double TotalLoss { get; init; }

    /// <summary>What to do about it ("brake 12 metres later").</summary>
    public required string Advice { get; init; }
}

/// <summary>A mistake made in the same corner on many laps: what the session summary leads with.</summary>
public sealed record RecurringMistake
{
    public required string CornerId { get; init; }

    public required string CornerName { get; init; }

    public required string Kind { get; init; }

    public int Laps { get; init; }

    public int OutOf { get; init; }

    /// <summary>Median size, in the kind's unit.</summary>
    public double TypicalAmount { get; init; }

    public bool IsRamp { get; init; }

    public bool NoBraking { get; init; }

    public double TypicalLoss { get; init; }

    public double TotalLoss { get; init; }

    /// <summary>What went wrong, in words ("You braked about 12 m early on 6 of 9 laps").</summary>
    public required string Problem { get; init; }

    /// <summary>What to do ("Brake 12 metres later").</summary>
    public required string Advice { get; init; }
}

/// <summary>Speed and pedals through a corner on one pass, every <see cref="StepM"/> metres from <see cref="StartM"/>.</summary>
public sealed record CornerTrace
{
    public int Lap { get; init; }

    public int StartM { get; init; }

    public int StepM { get; init; }

    public float[] SpeedKmh { get; init; } = [];

    /// <summary>0–1.</summary>
    public float[] Throttle { get; init; } = [];

    /// <summary>0–1.</summary>
    public float[] Brake { get; init; } = [];
}
