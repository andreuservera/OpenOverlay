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

    /// <summary>A lap to coach against instead of this session's best (the driver's best lap ever
    /// for this track and car); null to coach against the session. See <see cref="CoachingSession.CoachingReference"/>.</summary>
    public References.ReferenceLap? Reference { get; init; }

    /// <summary>The car (iRacing's CarScreenName), for what is kept per track and car.</summary>
    public string? Car { get; init; }

    /// <summary>Where each corner's type is kept per track and car (<see cref="CornerTypeStore"/>);
    /// null to agree the types afresh every session. Needs <see cref="TrackKey"/> and <see cref="Car"/>.</summary>
    public CornerTypeStore? TypeStore { get; init; }
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

    // Bests come only from laps from this index on: it moves when the car is found to have changed.
    private int _referenceStart;

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

    /// <summary>Raised when the car is slower on every straight since an off or incident (likely
    /// damage): the lap number of the incident and the top-speed drop (0.027 = 2.7%). Best passes,
    /// best lap and theoretical best restart from the lap after it.</summary>
    public event Action<int, double>? CarChanged;

    /// <summary>The incident lap after which the car was found slower, when it was; bests only come
    /// from later laps.</summary>
    public int? CarChangedAfterLap { get; private set; }

    /// <summary>Raised when the corner map is set or replaced (provisional, agreed, or loaded).</summary>
    public event Action<CornerMap>? CornerMapChanged;

    /// <summary>Every lap recorded, clean or not.</summary>
    public IReadOnlyList<RecordedLap> Laps => _recorder.Laps;

    /// <summary>Clean laps, measured, in order.</summary>
    public IReadOnlyList<LapAnalysis> Analyses => _analyses.Where(a => a.IsCleanLap).ToList();

    /// <summary>Every measured lap, clean or with clean corners only, in order.</summary>
    public IReadOnlyList<LapAnalysis> AllAnalyses => _analyses;

    /// <summary>The measured laps the bests come from: every one, or only those after the car
    /// changed (<see cref="CarChangedAfterLap"/>).</summary>
    public IReadOnlyList<LapAnalysis> ReferenceAnalyses => _analyses.Skip(_referenceStart).ToList();

    /// <summary>The corners, once a clean lap has been driven or a saved map loaded; null before.</summary>
    public CornerMap? CornerMap { get; private set; }

    public CornerMapOrigin? CornerMapOrigin { get; private set; }

    /// <summary>The best clean pass through each corner so far, from any lap, by corner id
    /// (see <see cref="Corners.BestPasses"/>).</summary>
    public IReadOnlyDictionary<string, CornerPass> BestPasses => _best;

    /// <summary>
    /// What the post-lap debrief compares each corner with. Without a reference lap
    /// (<see cref="CoachingOptions.Reference"/>) it is <see cref="BestPasses"/>. With one, it is the
    /// passes of one lap: the reference lap, or this session's best lap once that is quicker; a
    /// corner the reference lap didn't drive cleanly falls back to its best pass of the session.
    /// The post-session report keeps comparing with the session's own bests.
    /// </summary>
    public IReadOnlyDictionary<string, CornerPass> CoachingReference => _coachingReference ?? _best;

    /// <summary>The lap time <see cref="CoachingReference"/> stands for: the reference lap's, or the
    /// session's best lap's when that is quicker or there is no reference; null before any lap.</summary>
    public double? CoachingReferenceLapTime =>
        _referencePasses is null ? BestLap?.LapTime
        : BestLap is { } b && b.LapTime < _options.Reference!.LapTime ? b.LapTime
        : _options.Reference!.LapTime;

    /// <summary>True while the debrief compares with the stored reference lap rather than this session.</summary>
    public bool CoachingAgainstReference => _referencePasses is not null && !(BestLap is { } b && b.LapTime < _options.Reference!.LapTime);

    private Dictionary<string, CornerPass>? _referencePasses;
    private Dictionary<string, CornerPass>? _coachingReference;

    /// <summary>Each corner's type, agreed over its clean passes (<see cref="CornerTypes.Consensus"/>),
    /// by corner id: it decides which checks run and stays put from lap to lap.</summary>
    public IReadOnlyDictionary<string, CornerType> CornerTypeById => _types;

    private Dictionary<string, CornerType> _types = new(StringComparer.Ordinal);

    /// <summary>The corner types saved for this track and car (<see cref="CoachingOptions.TypeStore"/>),
    /// by corner id: they win over what this session's passes say.</summary>
    public IReadOnlyDictionary<string, CornerType> SavedCornerTypes => _savedTypes;

    private Dictionary<string, CornerType> _savedTypes = new(StringComparer.Ordinal);
    private IReadOnlySet<string> _fixedTypes = new HashSet<string>(StringComparer.Ordinal);

    public RecordedLap? BestLap => _analyses.Skip(_referenceStart).Where(a => a.IsCleanLap).MinBy(a => a.Lap.LapTime)?.Lap;

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
        // The reference lap measured on this map, if it was driven on the same track length.
        _referencePasses = _options.Reference is { } reference && Math.Abs(reference.Trace.Length - map.TrackLengthMetres) <= 2
            ? CornerAnalyzer.Analyze(reference.Trace, map, 0, LapFlaw.None).Where(p => p.IsClean).ToDictionary(p => p.Corner.Name, StringComparer.Ordinal)
            : null;
        _coachingReference = null;
        _savedTypes = LoadSavedTypes(map);
        _best = new Dictionary<string, CornerPass>(StringComparer.Ordinal);
        _bestOutsideCorners = null;
        _bestRestOfStraight = new Dictionary<string, double>(StringComparer.Ordinal);
        _referenceStart = 0;
        CarChangedAfterLap = null;
        var laps = _analyses.Select(a => a.Lap).Concat(_waitingForMap).OrderBy(l => l.StartTime).ToList();
        _waitingForMap.Clear();
        _analyses.Clear();
        foreach (var lap in laps)
        {
            Measure(lap);
        }

        CornerMapChanged?.Invoke(map);
    }

    private Dictionary<string, CornerType> LoadSavedTypes(CornerMap map)
    {
        if (_options.TypeStore is not { } store || _options.TrackKey is not { } key || _options.Car is not { } car)
        {
            return new Dictionary<string, CornerType>(StringComparer.Ordinal);
        }

        _fixedTypes = store.Fixed(key, car, map);
        return new Dictionary<string, CornerType>(store.Load(key, car, map), StringComparer.Ordinal);
    }

    /// <summary>
    /// Saves the type of every corner whose first <see cref="CornerTypes.SettledAfterPasses"/> clean
    /// passes clearly agree on one (<see cref="CornerTypes.SavedShare"/>) and that has none saved yet,
    /// and replaces a saved type (unless it was fixed by hand) when this session's clean passes
    /// clearly show another. Not on a provisional map, whose corners may still be merged or split.
    /// </summary>
    private void SaveSettledTypes(IReadOnlyList<CornerPass> passes)
    {
        if (_options.TypeStore is not { } store || _options.TrackKey is not { } key || _options.Car is not { } car
            || CornerMap is not { } map || CornerMapOrigin == Coaching.CornerMapOrigin.SingleLap)
        {
            return;
        }

        var added = new Dictionary<string, CornerType>(StringComparer.Ordinal);
        var replaced = new Dictionary<string, CornerType>(StringComparer.Ordinal);
        foreach (var g in passes.Where(p => p.IsClean).GroupBy(p => p.Corner.Name, StringComparer.Ordinal))
        {
            var clean = g.OrderBy(p => p.LapNumber).ToList();
            if (clean.Count < CornerTypes.SettledAfterPasses)
            {
                continue;
            }

            var saved = _savedTypes.TryGetValue(g.Key, out var s) ? s : (CornerType?)null;
            var counted = saved is null ? clean.Take(CornerTypes.SettledAfterPasses).ToList() : clean;
            if (CornerTypes.Decisive(counted) is not { } type)
            {
                continue; // split between types: agreed afresh each session
            }

            if (saved is null)
            {
                added[g.Key] = type;
            }
            else if (saved != type && !_fixedTypes.Contains(g.Key))
            {
                replaced[g.Key] = type;
            }
        }

        if (added.Count == 0 && replaced.Count == 0)
        {
            return;
        }

        foreach (var (id, type) in added.Concat(replaced))
        {
            _savedTypes[id] = type;
        }

        try
        {
            if (added.Count > 0)
            {
                store.Save(key, car, map, added);
            }

            if (replaced.Count > 0)
            {
                store.Save(key, car, map, replaced, replace: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not saved: kept for this session, agreed again next time.
        }
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

        var analysis = new LapAnalysis(lap, passes, new Dictionary<string, double>(StringComparer.Ordinal))
        {
            OutsideCornersTime = CornerChecks.OutsideCornersTime(lap, CornerMap!),
            RunInFlaws = CornerChecks.RunInFlaws(lap, CornerMap!),
        };
        _analyses.Add(analysis);
        DetectCarChange();
        RecomputeBests();

        var loss = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pass in passes)
        {
            if (pass.IsClean && _best.TryGetValue(pass.Corner.Name, out var best))
            {
                loss[pass.Corner.Name] = pass.ScoreTime - best.ScoreTime;
            }
        }

        _analyses[^1] = analysis with { LossToBestSeconds = loss };
    }

    private void RecomputeBests()
    {
        var reference = _analyses.Skip(_referenceStart).ToList();
        var all = reference.SelectMany(a => a.Passes).ToList();
        _best = Corners.BestPasses.Select(all);
        _bestRestOfStraight = Corners.BestPasses.RestOfStraight(all);
        _bestOutsideCorners = reference.Select(a => a.OutsideCornersTime).Where(t => t is not null).Min();
        _types = all.GroupBy(p => p.Corner.Name, StringComparer.Ordinal)
            .Select(g => (g.Key, Type: CornerTypes.Consensus(g, _best.GetValueOrDefault(g.Key))))
            .Where(x => x.Type is not null)
            .ToDictionary(x => x.Key, x => x.Type!.Value, StringComparer.Ordinal);
        SaveSettledTypes(all);
        foreach (var (id, type) in _savedTypes)
        {
            _types[id] = type;
        }

        if (_referencePasses is not null)
        {
            // One lap's passes: the reference, or this session's best lap once it is quicker.
            var lap = CoachingAgainstReference
                ? _referencePasses
                : reference.Where(a => a.IsCleanLap).MinBy(a => a.Lap.LapTime)!.Passes.Where(p => p.IsClean).ToDictionary(p => p.Corner.Name, StringComparer.Ordinal);
            _coachingReference = new Dictionary<string, CornerPass>(_best, StringComparer.Ordinal);
            foreach (var (id, pass) in lap)
            {
                _coachingReference[id] = pass;
            }
        }
    }

    /// <summary>
    /// When the lap <see cref="CarChange.LapsToConfirm"/> laps after an off or incident has just been
    /// measured, compare top speeds before and after it; if every lap since is clearly slower, the
    /// references restart from the lap after the incident.
    /// </summary>
    private void DetectCarChange()
    {
        var j = _analyses.Count - 1 - CarChange.LapsToConfirm;
        if (j < _referenceStart || (_analyses[j].Lap.Flaws & (LapFlaw.OffTrack | LapFlaw.Incident)) == 0)
        {
            return;
        }

        var map = CornerMap!;
        var before = _analyses.Skip(_referenceStart).Take(j - _referenceStart)
            .Select(a => CarChange.ApproachSpeeds(a.Lap.Trace!, map))
            .Append(CarChange.ApproachSpeeds(_analyses[j].Lap.Trace!, map, beforeFirstIncident: true))
            .ToList();
        var drops = _analyses.Skip(j + 1).Select(a => CarChange.Drop(CarChange.ApproachSpeeds(a.Lap.Trace!, map), before)).ToList();
        if (drops.All(d => d is { } v && v <= -CarChange.DropShare))
        {
            _referenceStart = j + 1;
            CarChangedAfterLap = _analyses[j].Lap.LapNumber;
            CarChanged?.Invoke(_analyses[j].Lap.LapNumber, -drops.Average(d => d!.Value));
        }
    }
}
