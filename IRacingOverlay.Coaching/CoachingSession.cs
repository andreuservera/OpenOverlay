using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Telemetry;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.Coaching;

/// <summary>
/// A lap measured corner by corner, with how each clean corner compares to the best so far. Laps
/// with an off-track or incident are measured too: their untouched corners count (see
/// <see cref="CornerPass.Flaws"/>), only the lap as a whole doesn't.
/// </summary>
/// <param name="LossToBestSeconds">Time lost against the best pass, by corner id, for the clean passes only.</param>
public sealed record LapAnalysis(RecordedLap Lap, IReadOnlyList<CornerPass> Passes, IReadOnlyDictionary<string, double> LossToBestSeconds)
{
    /// <summary>Time from the line to the first corner when that stretch was clean; null otherwise.</summary>
    public double? OutsideCornersTime { get; init; }

    /// <summary>What voids the run from the line to the first corner; none when it was clean.</summary>
    public CornerPassFlaw RunInFlaws { get; init; }

    /// <summary>True when the whole lap was clean, so it counts as a lap (best lap, debrief).</summary>
    public bool IsCleanLap => Lap.IsClean;

    /// <summary>
    /// Every piece of the lap that can't be used, in lap order, with why: the run to the first
    /// corner, a corner, or the rest of a long straight after a corner. This is where an off or
    /// incident that only touched a straight shows up.
    /// </summary>
    public IReadOnlyList<(string Piece, CornerPassFlaw Flaws)> UncleanPieces()
    {
        var pieces = new List<(string, CornerPassFlaw)>();
        if (RunInFlaws != CornerPassFlaw.None && Passes.Count > 0)
        {
            pieces.Add(($"run to {Passes[0].Corner.DisplayName}", RunInFlaws));
        }

        foreach (var p in Passes)
        {
            if (!p.IsClean)
            {
                pieces.Add((p.Corner.DisplayName, p.Flaws));
            }

            if (!p.RestOfStraightClean)
            {
                pieces.Add(($"straight after {p.Corner.DisplayName}", p.RestOfStraightFlaws));
            }
        }

        return pieces;
    }
}

/// <summary>Where the current corner map came from.</summary>
public enum CornerMapOrigin
{
    /// <summary>Provisional, from the first clean lap only, until enough laps agree.</summary>
    SingleLap,

    /// <summary>Agreed from several clean laps this session (and saved, when a store is set).</summary>
    Consensus,

    /// <summary>Loaded from the saved map for this track: the same ids and names as last time.</summary>
    Saved,
}

/// <summary>Settings for a <see cref="CoachingSession"/>.</summary>
public sealed record CoachingOptions
{
    /// <summary>Track length when known; otherwise estimated from the telemetry.</summary>
    public double? TrackLengthMetres { get; init; }

    public CornerDetectionOptions Detection { get; init; } = new();

    /// <summary>iRacing's internal track name (WeekendInfo.TrackName), used to find the saved map.</summary>
    public string? TrackKey { get; init; }

    /// <summary>Where corner maps are saved and loaded; null to neither load nor save.</summary>
    public CornerMapStore? Store { get; init; }

    /// <summary>Real turn names for corners the saved map doesn't name (Lovely Track Data); null for
    /// none. <see cref="TrackTurnNames.Default"/> uses the files shipped with the coach.</summary>
    public TrackTurnNames? TurnNames { get; init; }

    /// <summary>False to ignore the saved map and agree a new one (it is still saved over).</summary>
    public bool LoadSavedMap { get; init; } = true;

    /// <summary>Clean laps the map is agreed from before it is fixed and saved.</summary>
    public int CornerMapLaps { get; init; } = 5;
}

/// <summary>
/// The core of the coach: records laps, learns the track's corners, and measures every lap corner by
/// corner against the best clean pass through each corner. Corners are judged one by one: a lap with
/// an off-track or incident doesn't count as a lap, but the corners it didn't touch still count
/// toward the best passes and the theoretical best (<see cref="CornerChecks"/>). The corner map
/// itself is only ever learnt from fully clean laps.
///
/// Corners: a saved map for the track is used as is, so T-numbers and names never move between
/// sessions. Without one, the first clean lap gives a provisional map so measuring can start, and
/// after <see cref="CoachingOptions.CornerMapLaps"/> clean laps the map is agreed from all of them
/// (<see cref="CornerMapBuilder"/>), every lap is re-measured against it, and it is saved.
///
/// It only consumes telemetry. To run it live, subscribe it to the existing connection
/// (<c>connection.TelemetryUpdated += (_, s) =&gt; session.Observe(s)</c>) — nothing in the
/// connection or the app has to change. To run it offline, feed it an .ibt file's frames.
/// Not thread-safe: call it from one thread.
/// </summary>
public sealed class CoachingSession
{
    private readonly LapRecorder _recorder;
    private readonly CoachingOptions _options;
    // Lap flaws that make a lap useless even corner by corner: the car was reset, towed or moved,
    // the telemetry stopped, or the lap wasn't driven at speed.
    private const LapFlaw Unusable = LapFlaw.Partial | LapFlaw.NotRacing | LapFlaw.NotOnTrack | LapFlaw.TelemetryGap | LapFlaw.Discontinuity;

    private readonly List<LapAnalysis> _analyses = [];
    private readonly List<RecordedLap> _waitingForMap = [];
    private Dictionary<string, CornerPass> _best = new(StringComparer.Ordinal);
    private double? _bestOutsideCorners;
    private Dictionary<string, double> _bestRestOfStraight = new(StringComparer.Ordinal);

    public CoachingSession(CoachingOptions? options = null)
    {
        _options = options ?? new CoachingOptions();
        _recorder = new LapRecorder(_options.TrackLengthMetres);
        _recorder.LapCompleted += OnLapCompleted;
    }

    /// <summary>Raised for every lap as it completes, clean or not, before it is measured.</summary>
    public event Action<RecordedLap>? LapRecorded;

    /// <summary>Raised after each clean lap has been measured.</summary>
    public event Action<LapAnalysis>? LapAnalyzed;

    /// <summary>Raised after a lap with an off-track, incident or pit stop has been measured: its
    /// clean corners already count toward the best passes.</summary>
    public event Action<LapAnalysis>? FlawedLapAnalyzed;

    /// <summary>Raised when the corner map is set or replaced (provisional, agreed, or loaded).</summary>
    public event Action<CornerMap>? CornerMapChanged;

    /// <summary>Every lap recorded, clean or not.</summary>
    public IReadOnlyList<RecordedLap> Laps => _recorder.Laps;

    /// <summary>Clean laps, measured, in order.</summary>
    public IReadOnlyList<LapAnalysis> Analyses => _analyses.Where(a => a.IsCleanLap).ToList();

    /// <summary>Every measured lap, clean or with clean corners only, in order.</summary>
    public IReadOnlyList<LapAnalysis> AllAnalyses => _analyses;

    /// <summary>The corners, once a clean lap has been driven or a saved map loaded; null before.</summary>
    public CornerMap? CornerMap { get; private set; }

    public CornerMapOrigin? CornerMapOrigin { get; private set; }

    /// <summary>The best clean pass through each corner so far, from any lap, by corner id
    /// (see <see cref="Corners.BestPasses"/>).</summary>
    public IReadOnlyDictionary<string, CornerPass> BestPasses => _best;

    public RecordedLap? BestLap => _analyses.Where(a => a.IsCleanLap).MinBy(a => a.Lap.LapTime)?.Lap;

    /// <summary>
    /// The best pass of every corner chained into one lap, each with the straight after it: what
    /// the driver has already shown they can do, including corners from laps that didn't count.
    /// Corners chained from different laps may not quite join up, so treat it as a target, not a
    /// promise. Null until every corner has a clean pass.
    /// </summary>
    public double? TheoreticalBestLapTime =>
        CornerMap is { } map ? Corners.BestPasses.TheoreticalLapTime(map, _best, _bestOutsideCorners, _bestRestOfStraight) : null;

    public void Observe(in TelemetryFrame frame) => _recorder.Observe(frame);

    public void Observe(TelemetrySnapshot snapshot) => _recorder.Observe(SnapshotFrames.FromSnapshot(snapshot));

    /// <summary>End of input (end of file, session over).</summary>
    public void Flush() => _recorder.Flush();

    /// <summary>Agree the corners from the chosen clean laps, re-measure every lap, and save the map
    /// when a store is set.</summary>
    public void RebuildCornerMap(IEnumerable<RecordedLap> laps, bool save = true)
    {
        var traces = laps.Where(l => l.IsClean && l.Trace is not null).Select(l => l.Trace!).ToList();
        if (traces.Count == 0)
        {
            throw new ArgumentException("None of the laps has a trace to learn corners from.", nameof(laps));
        }

        SetMap(CornerMapBuilder.Build(traces, _options.Detection), Coaching.CornerMapOrigin.Consensus);
        if (save)
        {
            SaveMap();
        }
    }

    private void OnLapCompleted(RecordedLap lap)
    {
        LapRecorded?.Invoke(lap);
        if (lap.Trace is null || (lap.Flaws & Unusable) != 0)
        {
            return;
        }

        if (!lap.IsClean)
        {
            // Corners are only ever learnt from clean laps; until there is a map, keep the lap so
            // its clean corners count once there is one.
            if (CornerMap is null)
            {
                _waitingForMap.Add(lap);
                return;
            }

            Measure(lap);
            FlawedLapAnalyzed?.Invoke(_analyses[^1]);
            return;
        }

        if (CornerMap is null)
        {
            if (_options.LoadSavedMap && _options.Store is { } store && _options.TrackKey is { } key && store.Load(key, lap.Trace.Length) is { } saved)
            {
                SetMap(saved, Coaching.CornerMapOrigin.Saved);
            }
            else
            {
                SetMap(CornerDetector.Detect(lap.Trace, _options.Detection), Coaching.CornerMapOrigin.SingleLap);
            }
        }

        Measure(lap);

        var cleanLaps = _analyses.Where(a => a.IsCleanLap).Select(a => a.Lap).ToList();
        if (CornerMapOrigin == Coaching.CornerMapOrigin.SingleLap && cleanLaps.Count >= Math.Max(2, _options.CornerMapLaps))
        {
            RebuildCornerMap(cleanLaps);
        }

        LapAnalyzed?.Invoke(_analyses[^1]);
    }

    private void SetMap(CornerMap map, CornerMapOrigin origin)
    {
        if (_options.TurnNames is { } names && _options.TrackKey is { } key)
        {
            map = names.Apply(map, key);
        }

        CornerMap = map;
        CornerMapOrigin = origin;
        _best = new Dictionary<string, CornerPass>(StringComparer.Ordinal);
        _bestOutsideCorners = null;
        _bestRestOfStraight = new Dictionary<string, double>(StringComparer.Ordinal);
        var laps = _analyses.Select(a => a.Lap).Concat(_waitingForMap).OrderBy(l => l.StartTime).ToList();
        _waitingForMap.Clear();
        _analyses.Clear();
        foreach (var lap in laps)
        {
            Measure(lap);
        }

        CornerMapChanged?.Invoke(map);
    }

    private void SaveMap()
    {
        if (_options.Store is not { } store || _options.TrackKey is not { } key || CornerMap is null)
        {
            return;
        }

        try
        {
            store.Save(key, CornerMap);
            // Pick up names already set for these corners in the saved file.
            if (store.Load(key) is { } reloaded && reloaded.Corners.Count == CornerMap.Corners.Count && reloaded.Corners.Any(c => c.Label is not null))
            {
                SetMap(new CornerMap(CornerMap.TrackLengthMetres,
                    CornerMap.Corners.Zip(reloaded.Corners, (c, r) => c with { Label = r.Label, LabelIsLookedUp = false }).ToList(),
                    CornerMap.LapsUsed), CornerMapOrigin!.Value);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not being able to save only costs stable ids next session; coaching carries on.
        }
    }

    private void Measure(RecordedLap lap)
    {
        var passes = CornerAnalyzer.Analyze(lap.Trace!, CornerMap!, lap.LapNumber, lap.Flaws).ToList();

        // A cut can't be seen in one pass alone: compare its path with the corner's earlier clean passes.
        for (var i = 0; i < passes.Count; i++)
        {
            var p = passes[i];
            var earlier = _analyses.SelectMany(a => a.Passes).Where(o => o.IsClean && o.Corner.Name == p.Corner.Name);
            if (CornerChecks.IsShortcut(p, earlier))
            {
                passes[i] = p with { Flaws = p.Flaws | CornerPassFlaw.Shortcut };
            }
        }

        if (CornerChecks.OutsideCornersTime(lap, CornerMap!) is { } outside && (_bestOutsideCorners is null || outside < _bestOutsideCorners))
        {
            _bestOutsideCorners = outside;
        }

        var all = _analyses.SelectMany(a => a.Passes).Concat(passes).ToList();
        _best = Corners.BestPasses.Select(all);
        _bestRestOfStraight = Corners.BestPasses.RestOfStraight(all);
        var loss = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pass in passes)
        {
            if (pass.IsClean && _best.TryGetValue(pass.Corner.Name, out var best))
            {
                loss[pass.Corner.Name] = pass.ScoreTime - best.ScoreTime;
            }
        }

        _analyses.Add(new LapAnalysis(lap, passes, loss)
        {
            OutsideCornersTime = CornerChecks.OutsideCornersTime(lap, CornerMap!),
            RunInFlaws = CornerChecks.RunInFlaws(lap, CornerMap!),
        });
    }
}
