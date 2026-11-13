using IRacingOverlay.Coaching.Advice;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;

namespace IRacingOverlay.Coaching.Reports;

/// <summary>Who, where and when, for a <see cref="SessionReport"/>: what the telemetry alone doesn't say.</summary>
public sealed record SessionReportInfo
{
    public required string Id { get; init; }

    /// <summary>"live" or "ibt".</summary>
    public required string Source { get; init; }

    public string? SourceFile { get; init; }

    public DateTime StartedUtc { get; init; }

    public string? TrackKey { get; init; }

    public string? TrackName { get; init; }

    public string? CarName { get; init; }

    public string? SessionType { get; init; }
}

/// <summary>Settings for <see cref="SessionReportBuilder"/>.</summary>
public sealed record SessionReportOptions
{
    /// <summary>A pass that lost less than this isn't counted as a mistake, seconds (lap-to-lap noise).</summary>
    public double MinLossSeconds { get; init; } = 0.05;

    /// <summary>A mistake is recurring when it is the main finding on at least this share of the
    /// clean passes it could have come up on…</summary>
    public double RecurringShare { get; init; } = 0.4;

    /// <summary>…and on at least this many laps.</summary>
    public int RecurringMinLaps { get; init; } = 2;

    /// <summary>Passes needed in a corner before anything there is called a habit.</summary>
    public int RecurringMinPasses { get; init; } = 3;

    /// <summary>Recurring mistakes listed at most, and at most per corner.</summary>
    public int MaxMistakes { get; init; } = 6;

    public int MaxMistakesPerCorner { get; init; } = 2;

    /// <summary>Spacing of the saved corner traces, metres.</summary>
    public int TraceStepMetres { get; init; } = 2;

    public DiagnosisOptions Diagnosis { get; init; } = new();
}

/// <summary>
/// Turns a <see cref="CoachingSession"/> into its post-session <see cref="SessionReport"/>. Every clean
/// pass through each corner is compared with the session's best pass through it (the same
/// <see cref="CornerDiagnoser"/> the post-lap debrief uses), and a finding that is the main one on
/// many laps in the same corner becomes a recurring mistake. Only reads the session.
/// </summary>
public static class SessionReportBuilder
{
    private const double MsToKmh = 3.6;

    public static SessionReport Build(CoachingSession session, SessionReportInfo info, SessionReportOptions? options = null, DateTime? nowUtc = null)
    {
        var o = options ?? new SessionReportOptions();
        var reference = session.ReferenceAnalyses;
        var referenceSet = new HashSet<RecordedLap>(reference.Select(a => a.Lap), ReferenceEqualityComparer.Instance);
        var analysisOf = session.AllAnalyses.ToDictionary<LapAnalysis, RecordedLap, LapAnalysis>(a => a.Lap, a => a, ReferenceEqualityComparer.Instance);
        var best = session.BestPasses;
        var bestLap = session.BestLap;
        var notes = new List<string>();

        var corners = new List<CornerReport>();
        if (session.CornerMap is { } map)
        {
            var grip = GripEnvelope.Learn(reference.Where(a => a.Lap.Trace is not null).Select(a => a.Lap.Trace!));
            for (var i = 0; i < map.Corners.Count; i++)
            {
                corners.Add(BuildCorner(map.Corners[i], reference, best, bestLap, o, grip));
            }
        }
        else
        {
            notes.Add("No clean lap was driven, so the corners couldn't be learned.");
        }

        // Laps: delta and corner loss against the final bests, which is what the page compares with.
        var laps = new List<LapRow>();
        foreach (var lap in session.Laps)
        {
            var compared = referenceSet.Contains(lap);
            analysisOf.TryGetValue(lap, out var a);
            double? loss = null;
            if (compared && a is not null && best.Count > 0)
            {
                loss = a.Passes.Where(p => p.IsClean && best.ContainsKey(p.Corner.Name))
                    .Sum(p => Math.Max(0, p.ScoreTime - best[p.Corner.Name].ScoreTime));
            }

            laps.Add(new LapRow
            {
                Number = lap.LapNumber,
                Time = lap.LapTime,
                Clean = lap.IsClean,
                Flaws = FlawNames(lap.Flaws),
                DeltaToBest = lap.IsClean && compared && bestLap is not null ? Math.Max(0, lap.LapTime - bestLap.LapTime) : null,
                CornerLoss = loss,
                UncleanPieces = a?.UncleanPieces().Select(u => $"{u.Piece}: {u.Flaws}").ToList() ?? [],
                Compared = compared,
            });
        }

        var cleanTimes = reference.Where(a => a.IsCleanLap).Select(a => a.Lap.LapTime).ToList();
        double? averageGap = bestLap is not null && cleanTimes.Count > 1 ? cleanTimes.Average(t => t - bestLap.LapTime) : null;
        double? spread = cleanTimes.Count > 1 ? StdDev(cleanTimes) : null;

        if (session.CarChangedAfterLap is { } changed)
        {
            notes.Add($"The car was slower on every straight after lap {changed} (likely damage), so only the laps after it are compared.");
        }

        if (session.CornerMap is not null && cleanTimes.Count < 3)
        {
            notes.Add(cleanTimes.Count == 1
                ? "Only one clean lap: drive a few more for the recurring mistakes to show."
                : $"Only {cleanTimes.Count} clean laps: drive a few more for the recurring mistakes to show.");
        }

        var mistakes = corners
            .SelectMany(c => c.Findings.Where(f => IsRecurring(f, o)).Take(o.MaxMistakesPerCorner).Select(f => (Corner: c, Finding: f)))
            .OrderByDescending(x => x.Finding.TotalLoss)
            .Take(o.MaxMistakes)
            .Select(x => new RecurringMistake
            {
                CornerId = x.Corner.Id,
                CornerName = x.Corner.Name,
                Kind = x.Finding.Kind,
                Laps = x.Finding.Laps,
                OutOf = x.Finding.OutOf,
                TypicalAmount = x.Finding.TypicalAmount,
                IsRamp = x.Finding.IsRamp,
                NoBraking = x.Finding.NoBraking,
                TypicalLoss = x.Finding.TypicalLoss,
                TotalLoss = x.Finding.TotalLoss,
                Problem = SessionSummaryText.Problem(x.Finding),
                Advice = SessionSummaryText.Capital(x.Finding.Advice),
            })
            .ToList();

        return new SessionReport
        {
            Id = info.Id,
            Source = info.Source,
            SourceFile = info.SourceFile,
            StartedUtc = info.StartedUtc,
            UpdatedUtc = nowUtc ?? DateTime.UtcNow,
            TrackKey = info.TrackKey,
            TrackName = info.TrackName,
            CarName = info.CarName,
            SessionType = info.SessionType,
            TrackLengthMetres = session.CornerMap?.TrackLengthMetres ?? session.Laps.Select(l => l.Trace?.Length ?? 0).DefaultIfEmpty(0).Max(),
            Laps = laps,
            BestLapNumber = bestLap?.LapNumber,
            BestLapTime = bestLap?.LapTime,
            TheoreticalBestLapTime = session.TheoreticalBestLapTime,
            CarChangedAfterLap = session.CarChangedAfterLap,
            AverageGapToBest = averageGap,
            CleanLapSpread = spread,
            Corners = corners,
            Mistakes = mistakes,
            Notes = notes,
        };
    }

    internal static bool IsRecurring(FindingTally f, SessionReportOptions o) =>
        f.OutOf >= o.RecurringMinPasses
        && f.Laps >= o.RecurringMinLaps
        && f.Laps >= Math.Ceiling(o.RecurringShare * f.OutOf - 1e-9);

    private static CornerReport BuildCorner(Corner corner, IReadOnlyList<LapAnalysis> reference, IReadOnlyDictionary<string, CornerPass> bests, RecordedLap? bestLap, SessionReportOptions o, GripEnvelope? grip = null)
    {
        var passes = new List<(CornerPass Pass, LapAnalysis Lap)>();
        foreach (var a in reference)
        {
            foreach (var p in a.Passes)
            {
                if (p.Corner.Name == corner.Name)
                {
                    passes.Add((p, a));
                }
            }
        }

        bests.TryGetValue(corner.Name, out var best);
        var type = CornerTypes.Consensus(passes.Select(x => x.Pass), best);
        var rows = new List<PassRow>();
        var compared = new List<(CornerPass Pass, LapAnalysis Lap, double Loss, IReadOnlyList<CornerFinding> Findings)>();
        foreach (var (p, a) in passes)
        {
            var isBest = best is not null && ReferenceEquals(p, best);
            IReadOnlyList<CornerFinding> findings = [];
            double? loss = null;
            if (p.IsClean && best is not null)
            {
                loss = Math.Max(0, p.ScoreTime - best.ScoreTime);
                if (!isBest)
                {
                    findings = CornerDiagnoser.Diagnose(p, best, o.Diagnosis, type);
                    compared.Add((p, a, loss.Value, findings));
                }
            }

            rows.Add(Row(p, loss, isBest, findings));
        }

        // Tally each pass's main finding (the cause the debrief would name), when it cost real time.
        var tallies = compared
            .Where(c => c.Findings.Count > 0 && c.Loss >= o.MinLossSeconds)
            .GroupBy(c => c.Findings[0].Kind)
            .Select(g =>
            {
                var amounts = g.Select(c => c.Findings[0].Amount).ToList();
                var ramp = g.Count(c => c.Findings[0].IsRamp) * 2 > g.Count();
                var noBraking = g.Count(c => c.Findings[0].NoBraking) * 2 > g.Count();
                var typical = new CornerFinding(g.Key, Median(amounts)) { IsRamp = ramp, NoBraking = noBraking };
                return new FindingTally
                {
                    Kind = g.Key.ToString(),
                    Laps = g.Count(),
                    OutOf = compared.Count,
                    TypicalAmount = typical.Amount,
                    IsRamp = ramp,
                    NoBraking = noBraking,
                    TypicalLoss = Median(g.Select(c => c.Loss).ToList()),
                    TotalLoss = g.Sum(c => c.Loss),
                    Advice = DebriefText.Advice(typical),
                };
            })
            .OrderByDescending(t => t.Laps)
            .ThenByDescending(t => t.TotalLoss)
            .ToList();

        // The typical pass: the one in the middle by loss, so the page shows a normal lap, not the worst.
        var typicalPass = compared.OrderBy(c => c.Loss).Skip((compared.Count - 1) / 2).Select(c => ((CornerPass, LapAnalysis)?)(c.Pass, c.Lap)).FirstOrDefault();
        var bestEntry = best is null ? ((CornerPass, LapAnalysis)?)null : passes.Where(x => ReferenceEquals(x.Pass, best)).Select(x => ((CornerPass, LapAnalysis)?)x).FirstOrDefault();
        double? bestLapLoss = null;
        if (best is not null && bestLap is not null && passes.FirstOrDefault(x => ReferenceEquals(x.Lap.Lap, bestLap)) is { Pass: { IsClean: true } blp })
        {
            bestLapLoss = Math.Max(0, blp.ScoreTime - best.ScoreTime);
        }

        var decided = CornerStats.DecidedBy(passes.Where(x => x.Pass.IsClean).Select(x => x.Pass).ToList());
        return new CornerReport
        {
            Id = corner.Name,
            Name = corner.DisplayName,
            // "slow right · slow", "fast left · lift": how it looks and what decides its time.
            Kind = best is null ? null : string.Join(" · ", new[] { DebriefText.Describe(best), type is { } ty ? CornerTypes.Label(ty) : null }.Where(k => k is not null)),
            Direction = best?.Direction ?? 0,
            Type = type?.ToString(),
            StartM = corner.StartM,
            ApexM = corner.ApexM,
            EndM = corner.EndM,
            CleanPasses = passes.Count(x => x.Pass.IsClean),
            BestLap = best?.LapNumber,
            BestTime = best?.ScoreTime,
            MedianLoss = compared.Count > 0 ? NormalLoss(compared.Select(c => c.Loss).ToList()) : null,
            BestLapLoss = bestLapLoss,
            Best = best is null ? null : rows[passes.FindIndex(x => ReferenceEquals(x.Pass, best))],
            Typical = typicalPass is { } tp ? rows[passes.FindIndex(x => ReferenceEquals(x.Pass, tp.Item1))] : null,
            Passes = rows,
            Findings = tallies,
            BestTrace = bestEntry is { } be ? Trace(be.Item1, be.Item2.Lap, corner, o.TraceStepMetres) : null,
            TypicalTrace = typicalPass is { } t ? Trace(t.Item1, t.Item2.Lap, corner, o.TraceStepMetres) : null,
            // With two passes the normal lap is the better one (see NormalLoss), so its split is too.
            LossByPhase = PhaseLosses((compared.Count == 2 ? compared.OrderBy(c => c.Loss).Take(1) : compared).Select(c => (c.Pass, c.Lap)).ToList(), bestEntry),
            // A flat corner uses little of the grip by nature, so a share of it says nothing.
            Grip = type is CornerType.Flat ? null : GripOf(grip, bestEntry, typicalPass),
            TimeSpread = CornerStats.Spread(passes.Where(x => x.Pass.IsClean).Select(x => x.Pass.ScoreTime).ToList()),
            BrakePointSpreadM = type is CornerType.Flat or CornerType.Lift ? null
                : CornerStats.Spread(passes.Where(x => x.Pass.IsClean && x.Pass.BrakePointM is not null).Select(x => (double)x.Pass.BrakePointM!.Value).ToList()),
            LiftPointSpreadM = type is CornerType.Lift
                ? CornerStats.Spread(passes.Where(x => x.Pass.IsClean && x.Pass.LiftPointM is not null && !x.Pass.LiftCarriedOver).Select(x => (double)x.Pass.LiftPointM!.Value).ToList())
                : null,
            DecidedBy = DecidedByText(decided),
            DecidedByMeasure = decided is { } dm ? MeasureKey(dm.Measure) : null,
            DecidedByBetter = decided is { } db ? Better(db.Measure, db.R) : null,
        };
    }

    /// <summary>Median loss per phase over the compared passes; empty without a best pass trace.</summary>
    private static List<PhaseLossRow> PhaseLosses(List<(CornerPass Pass, LapAnalysis Lap)> compared, (CornerPass, LapAnalysis)? bestEntry)
    {
        if (bestEntry is not { } be || be.Item2.Lap.Trace is not { } bestTrace || compared.Count == 0)
        {
            return [];
        }

        var splits = compared.Where(c => c.Lap.Lap.Trace is not null)
            .Select(c => LossBreakdown.Split(c.Lap.Lap.Trace!, bestTrace, be.Item1)).ToList();
        if (splits.Count == 0)
        {
            return [];
        }

        return Enum.GetValues<CornerPhase>()
            .Select(ph => new PhaseLossRow { Phase = PhaseName(ph), Key = ph.ToString(), Seconds = Math.Round(Median(splits.Select(s => s[ph]).ToList()), 3) })
            .ToList();
    }

    internal static string PhaseName(CornerPhase phase) => phase switch
    {
        CornerPhase.Approach => "Approach",
        CornerPhase.Braking => "Braking",
        CornerPhase.Rotation => "Turn-in to throttle",
        CornerPhase.Traction => "Traction",
        _ => "Straight after",
    };

    /// <summary>Braking grip is only measured on a brake at least this hard…</summary>
    private const float MinGripBrake = 0.4f;

    /// <summary>…and this long, metres.</summary>
    private const int MinGripBrakeMetres = 20;

    private static GripUse? GripOf(GripEnvelope? grip, (CornerPass, LapAnalysis)? best, (CornerPass, LapAnalysis)? typical)
    {
        if (grip is null)
        {
            return null;
        }

        (double? Braking, double? Cornering) Use(CornerPass p, LapTrace? t)
        {
            if (t is null)
            {
                return (null, null);
            }

            // Only real braking: a light or short brake isn't using the grip, so a share of it says nothing.
            double? braking = p.BrakePointM is { } bp && p.BrakeReleaseM is { } br && p.PeakBrake >= MinGripBrake && p.BrakeLengthM >= MinGripBrakeMetres
                ? grip.Usage(t, bp, br)
                : null;
            var from = p.BrakeReleaseM ?? p.MinSpeedAtM - 20;
            var to = p.ThrottlePickupM is { } tp && tp > from ? tp : p.MinSpeedAtM + 20;
            return (braking, grip.Usage(t, from, to));
        }

        var b = best is { } be ? Use(be.Item1, be.Item2.Lap.Trace) : (null, null);
        var ty = typical is { } te ? Use(te.Item1, te.Item2.Lap.Trace) : (null, null);
        return new GripUse
        {
            BestBraking = Round(b.Braking),
            BestCornering = Round(b.Cornering),
            TypicalBraking = Round(ty.Braking),
            TypicalCornering = Round(ty.Cornering),
        };

        static double? Round(double? v) => v is { } x ? Math.Round(x, 3) : null;
    }

    private static string? DecidedByText((string Measure, double R)? d)
    {
        if (d is not { } x)
        {
            return null;
        }

        return Better(x.Measure, x.R) switch
        {
            "later" when x.Measure == "throttle pick-up" => "Throttle pick-up: turning the car longer before the throttle was quicker",
            var way => $"{SessionSummaryText.Capital(x.Measure)}: {way} was quicker",
        };
    }

    /// <summary>Which way of a measurement went with the quicker passes; r is against time, so
    /// negative means more of it, less time.</summary>
    internal static string Better(string measure, double r) => measure switch
    {
        "brake point" or "lift point" or "throttle pick-up" => r > 0 ? "earlier" : "later",
        _ => r < 0 ? "higher" : "lower",
    };

    internal static string MeasureKey(string measure) => measure switch
    {
        "brake point" => "brakePoint",
        "lift point" => "liftPoint",
        "minimum speed" => "minSpeed",
        "throttle pick-up" => "throttlePickup",
        "exit speed" => "exitSpeed",
        _ => "entrySpeed",
    };

    private static PassRow Row(CornerPass p, double? loss, bool isBest, IReadOnlyList<CornerFinding> findings) => new()
    {
        Lap = p.LapNumber,
        Time = p.ScoreTime,
        Loss = loss,
        Clean = p.IsClean,
        Flaws = p.IsClean ? null : p.Flaws.ToString(),
        IsBest = isBest,
        EntryKmh = Math.Round(p.EntrySpeed * MsToKmh, 1),
        BrakePointM = p.BrakePointM,
        PeakBrakePct = Math.Round(p.PeakBrake * 100, 0),
        TrailBrakeM = p.TrailBrakeM,
        MinSpeedKmh = Math.Round(p.MinSpeed * MsToKmh, 1),
        MinSpeedAtM = p.MinSpeedAtM,
        ThrottleM = p.ThrottlePickupM,
        ExitKmh = Math.Round(p.ExitSpeedPlus100 * MsToKmh, 1),
        Gear = p.GearAtApex,
        Findings = findings.Select(DebriefText.Advice).ToList(),
        Issues = findings.Select(f => new PassIssue(f.Kind.ToString(), Math.Round(f.Amount, 1), f.IsRamp) { NoBraking = f.NoBraking }).ToList(),
    };

    /// <summary>The pass's piece of the lap (from a little before braking to the end of its
    /// straight), sampled every <paramref name="step"/> metres.</summary>
    private static CornerTrace? Trace(CornerPass p, RecordedLap lap, Corner corner, int step)
    {
        if (lap.Trace is not { } t)
        {
            return null;
        }

        var start = Math.Clamp(p.ScoreEndM > p.ScoreStartM ? p.ScoreStartM : corner.StartM - CornerAnalyzer.LeadInMetres, 0, t.Length - 1);
        var end = Math.Clamp(p.ScoreEndM > p.ScoreStartM ? p.ScoreEndM : corner.EndM + 100, start + 1, t.Length - 1);
        step = Math.Max(1, step);
        var n = (end - start) / step + 1;
        var speed = new float[n];
        var throttle = new float[n];
        var brake = new float[n];
        for (var i = 0; i < n; i++)
        {
            var m = start + i * step;
            speed[i] = MathF.Round((float)(t.Speed[m] * MsToKmh), 1);
            throttle[i] = MathF.Round(t.Throttle[m], 2);
            brake[i] = MathF.Round(t.Brake[m], 2);
        }

        return new CornerTrace { Lap = lap.LapNumber, StartM = start, StepM = step, SpeedKmh = speed, Throttle = throttle, Brake = brake };
    }

    private static List<string> FlawNames(LapFlaw flaws) =>
        Enum.GetValues<LapFlaw>().Where(f => f != LapFlaw.None && flaws.HasFlag(f)).Select(f => f.ToString()).ToList();

    /// <summary>What a normal lap loses: the median, except that of two passes it is the smaller
    /// loss, so one bad pass (a lift, traffic) doesn't make up half of it.</summary>
    internal static double NormalLoss(List<double> values) => values.Count == 2 ? values.Min() : Median(values);

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }

    private static double StdDev(List<double> values)
    {
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }
}
