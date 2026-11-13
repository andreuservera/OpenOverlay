using IRacingOverlay.Coaching.Advice;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.History;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.References;
using IRacingOverlay.Coaching.Reports;
using IRacingOverlay.Coaching.Speech;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.Coaching;

/// <summary>
/// Coaching while the driver drives, fed from an iRacing connection: a session starts once iRacing
/// says which track this is (and again if the track changes), the report is saved after every lap
/// for the Coaching page, and with a voice the debrief and lap notes are spoken. Used by the CLI's
/// --live and by the app when live coaching is switched on.
///
/// Not thread-safe: call it from one thread at a time (the connection's reader thread).
/// </summary>
public sealed class LiveCoach
{
    private readonly CornerMapStore _maps;
    private readonly LiveReportSaver? _saver;
    private readonly TrackTurnNames? _names;
    private CoachingSession? _session;
    private string? _track;
    private string? _pendingTrack;
    private string? _pendingCar;
    private string? _sessionCar;
    private string? _trackName;
    private IracingSessionInfo? _sessionInfo;
    private SessionReportInfo? _reportInfo;
    private bool _reportDue;

    private readonly ReferenceLapStore? _references;
    private readonly CornerTypeStore? _types;
    private readonly SessionReportStore? _reports;
    private readonly FocusStore? _focus;
    private string? _car;

    /// <param name="references">Where each track and car's best lap ever is kept: every clean lap
    /// quicker than the stored one replaces it. Null keeps none.</param>
    /// <param name="types">Where each corner's type is kept per track and car; null agrees them afresh every session.</param>
    /// <param name="focus">The corners the driver pinned; with <paramref name="reports"/>, the focus
    /// plan (<see cref="FocusPlanner"/>) whose corners the debrief talks about first. Null for none.</param>
    public LiveCoach(CornerMapStore maps, SessionReportStore? reports, TrackTurnNames? names = null, ReferenceLapStore? references = null, CornerTypeStore? types = null, FocusStore? focus = null)
    {
        _types = types;
        _reports = reports;
        _focus = focus;
        _maps = maps;
        _saver = reports is null ? null : new LiveReportSaver(reports);
        _names = names;
        _references = references;
    }

    /// <summary>Coach against the best lap ever for this track and car (from the reference store)
    /// instead of this session's best. Read when a session starts.</summary>
    public bool CompareWithBestEver { get; set; }

    /// <summary>Says the debrief and lap notes; null to stay quiet. Can change at any time.</summary>
    public ISpeechOutput? Voice { get; set; }

    /// <summary>What the coach has to say, written out (the CLI prints it).</summary>
    public event Action<string>? Log;

    /// <summary>The session being coached; null until iRacing names the track.</summary>
    public CoachingSession? Session => _session;

    /// <summary>Where the last report was written; null before the first.</summary>
    public string? LastReportPath => _saver?.LastPath;

    public void OnSessionInfo(IracingSessionInfo info)
    {
        _sessionInfo = info;
        _trackName = info.WeekendInfo?.TrackDisplayName ?? info.WeekendInfo?.TrackName;
        _pendingTrack = info.WeekendInfo?.TrackName;
        var driver = info.DriverInfo;
        _pendingCar = driver?.Drivers.FirstOrDefault(d => d.CarIdx == driver.DriverCarIdx)?.CarScreenName;
    }

    public void OnTelemetry(TelemetrySnapshot snapshot)
    {
        // A new session for another track, or another car on the same one.
        if (_pendingTrack is { } t && (t != _track || _pendingCar != _sessionCar))
        {
            Start(t);
        }

        _session?.Observe(snapshot);
        if (_reportDue && _session is { Laps.Count: > 0 } coached && _saver is not null && _reportInfo is not null)
        {
            _reportDue = false;
            var type = _sessionInfo?.SessionInfo?.Sessions.FirstOrDefault(s => s.SessionNum == coached.Laps[^1].SessionNum)?.SessionType;
            _saver.Save(coached, _reportInfo with { SessionType = string.IsNullOrWhiteSpace(type) ? null : type });
        }
    }

    /// <summary>Waits for the report writes under way (at most a few seconds).</summary>
    public void Flush() => _saver?.Flush();

    /// <summary>The focus plan from the saved sessions at this track with this car, read off the
    /// reader thread (it reads every saved report); the debrief uses it from the lap after.</summary>
    private void LoadFocusPlan(PostLapCoach coach, string track)
    {
        if (_reports is not { } reports || _car is not { } car)
        {
            return;
        }

        var focus = _focus;
        _ = Task.Run(() =>
        {
            try
            {
                if (TrackHistory.Build(reports.LoadAll(), track, car) is not { } history)
                {
                    return;
                }

                var plan = FocusPlanner.Plan(history, focus?.Pinned(track, car));
                if (plan.Count > 0)
                {
                    coach.FocusApexM = plan.Select(p => p.ApexM).ToList();
                    Log?.Invoke("Focus this session: " + string.Join("; ", plan.Select(p => p.Text)) + ".");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // No plan: every corner is judged alike.
            }
        });
    }

    private void Start(string t)
    {
        _track = t;
        _sessionCar = _pendingCar;
        var driver = _sessionInfo?.DriverInfo;
        _car = driver?.Drivers.FirstOrDefault(d => d.CarIdx == driver.DriverCarIdx)?.CarScreenName;
        var reference = CompareWithBestEver && _references is not null && _car is not null ? _references.Load(t, _car) : null;
        var session = new CoachingSession(new CoachingOptions { TrackKey = t, Store = _maps, TurnNames = _names, Reference = reference, Car = _car, TypeStore = _types });
        _session = session;
        var started = DateTime.UtcNow;
        _reportInfo = new SessionReportInfo
        {
            Id = SessionReportStore.NewId(started, t),
            Source = "live",
            StartedUtc = started,
            TrackKey = t,
            TrackName = _trackName ?? t,
            CarName = _car,
        };

        // Every clean lap quicker than the stored best ever becomes the new one.
        if (_references is not null && _car is { } car)
        {
            var references = _references;
            session.LapRecorded += lap =>
            {
                if (!lap.IsClean)
                {
                    return;
                }

                // Off the reader thread: it reads and maybe writes a few hundred KB.
                _ = Task.Run(() =>
                {
                    if (references.Offer(t, car, lap, "live") is { } kept && ReferenceEquals(kept.Trace, lap.Trace))
                    {
                        Log?.Invoke($"Lap {lap.LapNumber} is your best ever here: kept as the reference.");
                    }
                });
            };
        }

        // Saved after every lap (once it has been measured), so the report is there even if the
        // coach is stopped mid-session.
        session.LapRecorded += _ => _reportDue = true;
        session.CornerMapChanged += m =>
        {
            Log?.Invoke($"Corners for {t}: {m.Corners.Count} ({session.CornerMapOrigin})");
            if (session.CornerMapOrigin == CornerMapOrigin.Consensus)
            {
                Voice?.Say("Corners learned.");
            }
        };

        // Say why a lap got no debrief when the driver might expect one.
        session.LapRecorded += lap =>
        {
            if (session.CornerMap is not null)
            {
                return; // FlawedLapAnalyzed says it, with how many corners still count.
            }

            if (lap.IsClean || (lap.Flaws & (LapFlaw.Partial | LapFlaw.PitRoad | LapFlaw.StartLap | LapFlaw.NotOnTrack | LapFlaw.NotRacing)) != 0)
            {
                return;
            }

            var why = lap.Flaws.HasFlag(LapFlaw.OffTrack) ? "off track" : lap.Flaws.HasFlag(LapFlaw.Incident) ? "incident" : null;
            if (why is not null)
            {
                Log?.Invoke($"Lap {lap.LapNumber} not counted: {why}.");
                Voice?.Say($"Lap not counted, {why}.");
            }
        };
        session.CarChanged += (lap, drop) =>
        {
            var pct = Math.Round(drop * 100);
            Log?.Invoke($"Car slower on every straight since lap {lap} (top speed -{drop:P1}); comparing with laps from now on.");
            Voice?.Say($"The car looks damaged, {pct:0} percent down on top speed. Comparing with laps from now on.");
        };
        session.FlawedLapAnalyzed += a =>
        {
            var why = a.Lap.Flaws.HasFlag(LapFlaw.OffTrack) ? "off track" : a.Lap.Flaws.HasFlag(LapFlaw.Incident) ? "incident" : null;
            if (why is null)
            {
                return; // pit or start laps: nothing the driver needs to hear
            }

            var kept = a.Passes.Count(p => p.IsClean);
            var where = a.UncleanPieces().Select(u => u.Piece).FirstOrDefault();
            var said = kept == a.Passes.Count && where is not null
                ? $"Lap not counted, {why} on the {where}. All corners still count."
                : $"Lap not counted, {why}. {kept} of {a.Passes.Count} corners still count.";
            Log?.Invoke($"Lap {a.Lap.LapNumber}: {said} Not clean: {string.Join(", ", a.UncleanPieces().Select(u => $"{u.Piece} ({u.Flaws})"))}");
            Voice?.Say(said);
        };
        var coach = PostLapCoach.Attach(session, d =>
        {
            Log?.Invoke("");
            foreach (var l in DebriefText.Lines(d))
            {
                Log?.Invoke(l);
            }

            Voice?.Say(DebriefText.Spoken(d));
        });
        LoadFocusPlan(coach, t);
        Log?.Invoke($"Coaching at {t}. Drive clean laps; the debrief comes as you cross the line.");
        if (reference is not null)
        {
            Log?.Invoke($"Comparing with your best lap ever here: {SessionSummaryText.LapTime(reference.LapTime)} ({reference.RecordedUtc.ToLocalTime():d MMM yyyy}).");
        }
        else if (CompareWithBestEver)
        {
            Log?.Invoke("No best lap ever stored for this track and car yet: comparing with this session.");
        }

        // So the driver knows the coach is listening without looking at the screen.
        Voice?.Say($"Coach ready, {_trackName ?? t}.");
    }
}
