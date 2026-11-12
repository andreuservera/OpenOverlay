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
            for (var i = 0; i < map.Corners.Count; i++)
            {
                corners.Add(BuildCorner(map.Corners[i], reference, best, bestLap, o));
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

    private static CornerReport BuildCorner(Corner corner, IReadOnlyList<LapAnalysis> reference, IReadOnlyDictionary<string, CornerPass> bests, RecordedLap? bestLap, SessionReportOptions o)
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
                    findings = CornerDiagnoser.Diagnose(p, best, o.Diagnosis);
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
                var typical = new CornerFinding(g.Key, Median(amounts)) { IsRamp = ramp };
                return new FindingTally
                {
                    Kind = g.Key.ToString(),
                    Laps = g.Count(),
                    OutOf = compared.Count,
                    TypicalAmount = typical.Amount,
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

        return new CornerReport
        {
            Id = corner.Name,
            Name = corner.DisplayName,
            Kind = DebriefText.Describe(best),
            StartM = corner.StartM,
            ApexM = corner.ApexM,
            EndM = corner.EndM,
            CleanPasses = passes.Count(x => x.Pass.IsClean),
            BestLap = best?.LapNumber,
            BestTime = best?.ScoreTime,
            MedianLoss = compared.Count > 0 ? Median(compared.Select(c => c.Loss).ToList()) : null,
            BestLapLoss = bestLapLoss,
            Best = best is null ? null : rows[passes.FindIndex(x => ReferenceEquals(x.Pass, best))],
            Typical = typicalPass is { } tp ? rows[passes.FindIndex(x => ReferenceEquals(x.Pass, tp.Item1))] : null,
            Passes = rows,
            Findings = tallies,
            BestTrace = bestEntry is { } be ? Trace(be.Item1, be.Item2.Lap, corner, o.TraceStepMetres) : null,
            TypicalTrace = typicalPass is { } t ? Trace(t.Item1, t.Item2.Lap, corner, o.TraceStepMetres) : null,
        };
    }

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
