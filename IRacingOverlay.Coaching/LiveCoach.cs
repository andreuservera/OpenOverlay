using IRacingOverlay.Coaching.Advice;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;
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
    private string? _trackName;
    private IracingSessionInfo? _sessionInfo;
    private SessionReportInfo? _reportInfo;
    private bool _reportDue;

    public LiveCoach(CornerMapStore maps, SessionReportStore? reports, TrackTurnNames? names = null)
    {
        _maps = maps;
        _saver = reports is null ? null : new LiveReportSaver(reports);
        _names = names;
    }

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
    }

    public void OnTelemetry(TelemetrySnapshot snapshot)
    {
        if (_pendingTrack is { } t && t != _track)
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

    private void Start(string t)
    {
        _track = t;
        var session = new CoachingSession(new CoachingOptions { TrackKey = t, Store = _maps, TurnNames = _names });
        _session = session;
        var started = DateTime.UtcNow;
        var driver = _sessionInfo?.DriverInfo;
        _reportInfo = new SessionReportInfo
        {
            Id = SessionReportStore.NewId(started, t),
            Source = "live",
            StartedUtc = started,
            TrackKey = t,
            TrackName = _trackName ?? t,
            CarName = driver?.Drivers.FirstOrDefault(d => d.CarIdx == driver.DriverCarIdx)?.CarScreenName,
        };

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
        PostLapCoach.Attach(session, d =>
        {
            Log?.Invoke("");
            foreach (var l in DebriefText.Lines(d))
            {
                Log?.Invoke(l);
            }

            Voice?.Say(DebriefText.Spoken(d));
        });
        Log?.Invoke($"Coaching at {t}. Drive clean laps; the debrief comes as you cross the line.");
        // So the driver knows the coach is listening without looking at the screen.
        Voice?.Say($"Coach ready, {_trackName ?? t}.");
    }
}
