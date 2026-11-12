using IRacingOverlay.Coaching.Corners;

namespace IRacingOverlay.Coaching.Advice;

/// <summary>One corner worth working on, from one lap.</summary>
/// <param name="LossSeconds">Time lost in this corner against the best pass through it.</param>
/// <param name="Findings">What was different, the one to fix first at the top; may be empty when the
/// time went somewhere the measurements don't show.</param>
/// <param name="RepeatCount">On how many of the previous laps (see <see cref="CoachOptions.RepeatWindowLaps"/>)
/// the same first finding came up in this corner: a habit rather than a one-off.</param>
public sealed record CornerAdvice(Corner Corner, double LossSeconds, IReadOnlyList<CornerFinding> Findings, int RepeatCount)
{
    public CornerFinding? Main => Findings.Count > 0 ? Findings[0] : null;
}

/// <summary>What the coach says after a clean lap.</summary>
/// <param name="DeltaToBestSeconds">Lap time minus the session's best lap (0 on a new best).</param>
/// <param name="Corners">The corners that cost the most time, worst first.</param>
public sealed record LapDebrief(
    int LapNumber,
    double LapTime,
    double DeltaToBestSeconds,
    bool IsBestLap,
    IReadOnlyList<CornerAdvice> Corners)
{
    /// <summary>Time lost in the reported corners together.</summary>
    public double ReportedLossSeconds => Corners.Sum(c => c.LossSeconds);
}

/// <summary>Settings for a <see cref="PostLapCoach"/>.</summary>
public sealed record CoachOptions
{
    /// <summary>A corner is only worth mentioning if it lost at least this much, seconds. Below it,
    /// lap-to-lap noise and measuring error are as big as the gain.</summary>
    public double MinLossSeconds { get; init; } = 0.05;

    /// <summary>How many corners to report per lap at most.</summary>
    public int MaxCorners { get; init; } = 3;

    /// <summary>How many earlier laps to look back over for a repeated mistake.</summary>
    public int RepeatWindowLaps { get; init; } = 5;

    public DiagnosisOptions Diagnosis { get; init; } = new();
}

/// <summary>
/// Phase 1 of the coach, post-lap only: after each clean lap it picks the corners where the most time
/// was lost against the driver's best pass through them and says what was different (see
/// <see cref="CornerDiagnoser"/>). It remembers recent laps so a mistake made lap after lap can be
/// called out as a habit. Not thread-safe.
/// </summary>
public sealed class PostLapCoach
{
    private readonly CoachOptions _options;
    private readonly Dictionary<string, Queue<FindingKind?>> _history = new(StringComparer.Ordinal);

    public PostLapCoach(CoachOptions? options = null)
    {
        _options = options ?? new CoachOptions();
    }

    /// <summary>Debriefs every clean lap of <paramref name="session"/> as it is measured, and forgets
    /// its history when the session's corners change (the old corner ids would no longer match).</summary>
    public static PostLapCoach Attach(CoachingSession session, Action<LapDebrief> onDebrief, CoachOptions? options = null)
    {
        var coach = new PostLapCoach(options);
        session.CornerMapChanged += _ => coach.Reset();
        session.LapAnalyzed += a => onDebrief(coach.Debrief(a, session.BestPasses, session.BestLap?.LapTime ?? a.Lap.LapTime));
        return coach;
    }

    public void Reset() => _history.Clear();

    /// <param name="lap">The lap to debrief.</param>
    /// <param name="bestPasses">The best pass through each corner so far, by corner id, this lap included.</param>
    /// <param name="bestLapTime">The best lap time so far, this lap included.</param>
    public LapDebrief Debrief(LapAnalysis lap, IReadOnlyDictionary<string, CornerPass> bestPasses, double bestLapTime)
    {
        var advice = new List<CornerAdvice>();
        foreach (var pass in lap.Passes)
        {
            var id = pass.Corner.Name;
            if (!bestPasses.TryGetValue(id, out var best))
            {
                continue;
            }

            var loss = pass.SegmentTime - best.SegmentTime;
            var findings = CornerDiagnoser.Diagnose(pass, best, _options.Diagnosis);
            var main = loss >= _options.MinLossSeconds && findings.Count > 0 ? findings[0].Kind : (FindingKind?)null;
            var history = _history.TryGetValue(id, out var h) ? h : _history[id] = new Queue<FindingKind?>();
            var repeats = main is null ? 0 : history.Count(k => k == main);
            history.Enqueue(main);
            while (history.Count > _options.RepeatWindowLaps)
            {
                history.Dequeue();
            }

            if (loss >= _options.MinLossSeconds)
            {
                advice.Add(new CornerAdvice(pass.Corner, loss, findings, repeats));
            }
        }

        var worst = advice.OrderByDescending(a => a.LossSeconds).Take(_options.MaxCorners).ToList();
        var delta = Math.Max(0, lap.Lap.LapTime - bestLapTime);
        return new LapDebrief(lap.Lap.LapNumber, lap.Lap.LapTime, delta, delta <= 0, worst);
    }
}
