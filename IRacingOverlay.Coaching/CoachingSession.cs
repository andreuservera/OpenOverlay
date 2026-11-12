using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Telemetry;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.Coaching;

/// <summary>A clean lap measured corner by corner, with how each corner compares to the best so far.</summary>
public sealed record LapAnalysis(RecordedLap Lap, IReadOnlyList<CornerPass> Passes, IReadOnlyDictionary<string, double> LossToBestSeconds);

/// <summary>
/// Phase 0 of the coach: records laps, learns the track's corners from the first clean lap, and
/// measures every clean lap corner by corner against the session's best pass through each corner.
/// It gives no advice yet; it produces the numbers advice will be built on.
///
/// It only consumes telemetry. To run it live, subscribe it to the existing connection
/// (<c>connection.TelemetryUpdated += (_, s) =&gt; session.Observe(s)</c>) — nothing in the
/// connection or the app has to change. To run it offline, feed it an .ibt file's frames.
/// Not thread-safe: call it from one thread.
/// </summary>
public sealed class CoachingSession
{
    private readonly LapRecorder _recorder;
    private readonly CornerDetectionOptions _detection;
    private readonly List<LapAnalysis> _analyses = [];
    private readonly Dictionary<string, CornerPass> _best = new(StringComparer.Ordinal);

    public CoachingSession(double? trackLengthMetres = null, CornerDetectionOptions? detection = null)
    {
        _recorder = new LapRecorder(trackLengthMetres);
        _detection = detection ?? new CornerDetectionOptions();
        _recorder.LapCompleted += OnLapCompleted;
    }

    /// <summary>Raised after each clean lap has been measured.</summary>
    public event Action<LapAnalysis>? LapAnalyzed;

    /// <summary>Every lap recorded, clean or not.</summary>
    public IReadOnlyList<RecordedLap> Laps => _recorder.Laps;

    /// <summary>Clean laps, measured, in order.</summary>
    public IReadOnlyList<LapAnalysis> Analyses => _analyses;

    /// <summary>The corners, once a clean lap has been driven; null before.</summary>
    public CornerMap? CornerMap { get; private set; }

    /// <summary>The lap the corner map was learned from.</summary>
    public RecordedLap? CornerMapSource { get; private set; }

    /// <summary>Fastest pass through each corner so far, by corner name.</summary>
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

    /// <summary>Re-learn the corners from a chosen lap (e.g. the best one) and re-measure every clean lap.</summary>
    public void RebuildCornerMap(RecordedLap source)
    {
        if (source.Trace is null)
        {
            throw new ArgumentException("The lap has no trace to learn corners from.", nameof(source));
        }

        CornerMap = CornerDetector.Detect(source.Trace, _detection);
        CornerMapSource = source;
        _best.Clear();
        var laps = _analyses.Select(a => a.Lap).ToList();
        _analyses.Clear();
        foreach (var lap in laps)
        {
            Measure(lap, raise: false);
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
            CornerMap = CornerDetector.Detect(lap.Trace, _detection);
            CornerMapSource = lap;
        }

        Measure(lap, raise: true);
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
