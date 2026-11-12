using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Telemetry;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.Coaching;

/// <summary>A clean lap measured corner by corner, with how each corner compares to the best so far.</summary>
public sealed record LapAnalysis(RecordedLap Lap, IReadOnlyList<CornerPass> Passes, IReadOnlyDictionary<string, double> LossToBestSeconds);

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

    /// <summary>Clean laps the map is agreed from before it is fixed and saved.</summary>
    public int CornerMapLaps { get; init; } = 5;
}

/// <summary>
/// Phase 0 of the coach: records laps, learns the track's corners, and measures every clean lap
/// corner by corner against the session's best pass through each corner. It gives no advice yet; it
/// produces the numbers advice will be built on.
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
    private readonly List<LapAnalysis> _analyses = [];
    private readonly Dictionary<string, CornerPass> _best = new(StringComparer.Ordinal);

    public CoachingSession(CoachingOptions? options = null)
    {
        _options = options ?? new CoachingOptions();
        _recorder = new LapRecorder(_options.TrackLengthMetres);
        _recorder.LapCompleted += OnLapCompleted;
    }

    /// <summary>Raised after each clean lap has been measured.</summary>
    public event Action<LapAnalysis>? LapAnalyzed;

    /// <summary>Raised when the corner map is set or replaced (provisional, agreed, or loaded).</summary>
    public event Action<CornerMap>? CornerMapChanged;

    /// <summary>Every lap recorded, clean or not.</summary>
    public IReadOnlyList<RecordedLap> Laps => _recorder.Laps;

    /// <summary>Clean laps, measured, in order.</summary>
    public IReadOnlyList<LapAnalysis> Analyses => _analyses;

    /// <summary>The corners, once a clean lap has been driven or a saved map loaded; null before.</summary>
    public CornerMap? CornerMap { get; private set; }

    public CornerMapOrigin? CornerMapOrigin { get; private set; }

    /// <summary>Fastest pass through each corner so far, by corner id.</summary>
    public IReadOnlyDictionary<string, CornerPass> BestPasses => _best;

    public RecordedLap? BestLap => _analyses.Count == 0 ? null : _analyses.MinBy(a => a.Lap.LapTime)!.Lap;

    /// <summary>
    /// The best lap with every corner swapped for the best pass through it: what the driver has
    /// already shown they can do. Corners chained from different laps may not quite join up, so treat
    /// it as a target, not a promise.
    /// </summary>
    public double? TheoreticalBestLapTime
    {
        get
        {
            if (BestLap is not { } best)
            {
                return null;
            }

            var analysis = _analyses.First(a => ReferenceEquals(a.Lap, best));
            var gain = analysis.Passes.Sum(p => _best.TryGetValue(p.Corner.Name, out var b) ? p.SegmentTime - b.SegmentTime : 0);
            return best.LapTime - gain;
        }
    }

    public void Observe(in TelemetryFrame frame) => _recorder.Observe(frame);

    public void Observe(TelemetrySnapshot snapshot) => _recorder.Observe(SnapshotFrames.FromSnapshot(snapshot));

    /// <summary>End of input (end of file, session over).</summary>
    public void Flush() => _recorder.Flush();

    /// <summary>Agree the corners from the chosen clean laps, re-measure every lap, and save the map
    /// when a store is set.</summary>
    public void RebuildCornerMap(IEnumerable<RecordedLap> laps, bool save = true)
    {
        var traces = laps.Where(l => l.Trace is not null).Select(l => l.Trace!).ToList();
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
        if (!lap.IsClean || lap.Trace is null)
        {
            return;
        }

        if (CornerMap is null)
        {
            if (_options.Store is { } store && _options.TrackKey is { } key && store.Load(key, lap.Trace.Length) is { } saved)
            {
                SetMap(saved, Coaching.CornerMapOrigin.Saved);
            }
            else
            {
                SetMap(CornerDetector.Detect(lap.Trace, _options.Detection), Coaching.CornerMapOrigin.SingleLap);
            }
        }

        Measure(lap, raise: false);

        if (CornerMapOrigin == Coaching.CornerMapOrigin.SingleLap && _analyses.Count >= Math.Max(2, _options.CornerMapLaps))
        {
            RebuildCornerMap(_analyses.Select(a => a.Lap));
        }

        LapAnalyzed?.Invoke(_analyses[^1]);
    }

    private void SetMap(CornerMap map, CornerMapOrigin origin)
    {
        CornerMap = map;
        CornerMapOrigin = origin;
        _best.Clear();
        var laps = _analyses.Select(a => a.Lap).ToList();
        _analyses.Clear();
        foreach (var lap in laps)
        {
            Measure(lap, raise: false);
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
                    CornerMap.Corners.Zip(reloaded.Corners, (c, r) => c with { Label = r.Label }).ToList(),
                    CornerMap.LapsUsed), CornerMapOrigin!.Value);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not being able to save only costs stable ids next session; coaching carries on.
        }
    }

    private void Measure(RecordedLap lap, bool raise)
    {
        var passes = CornerAnalyzer.Analyze(lap.Trace!, CornerMap!, lap.LapNumber);
        var loss = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pass in passes)
        {
            var name = pass.Corner.Name;
            if (!_best.TryGetValue(name, out var best) || pass.SegmentTime < best.SegmentTime)
            {
                _best[name] = pass;
                best = pass;
            }

            loss[name] = pass.SegmentTime - best.SegmentTime;
        }

        var analysis = new LapAnalysis(lap, passes, loss);
        _analyses.Add(analysis);
        if (raise)
        {
            LapAnalyzed?.Invoke(analysis);
        }
    }
}
