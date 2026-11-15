using System.Text.Json;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.References;
using IRacingOverlay.Coaching.Reports;
using IRacingOverlay.Coaching.Speech;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>The Coaching page's switches, saved in coaching\settings.json next to the reports.</summary>
public sealed record CoachingSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Coach while iRacing runs: record each session and save its report. Off by default,
    /// and when off the coach doesn't look at the telemetry at all.</summary>
    public bool LiveEnabled { get; init; }

    /// <summary>With live coaching on, say a short debrief after each clean lap.</summary>
    public bool SpeakDebrief { get; init; }

    /// <summary>Coach against the best lap ever for the track and car instead of this session's best.</summary>
    public bool CompareWithBestEver { get; init; }

    /// <summary>With live coaching on, a cue of two or three words just before each focus corner:
    /// on screen by default (nothing spoken), spoken, or off.</summary>
    public Advice.CueMode Cues { get; init; } = Advice.CueMode.Notice;

    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(SessionReportStore.DefaultDirectory)!, "settings.json");

    public static CoachingSettings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            return File.Exists(file) ? JsonSerializer.Deserialize<CoachingSettings>(File.ReadAllText(file), Json) ?? new() : new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not saved: the switch still holds for this run.
        }
    }
}

/// <summary>
/// Live coaching inside the app. The app hands over its iRacing connection once
/// (<see cref="Attach"/>); the coach only subscribes to it while live coaching is switched on, so
/// with it off nothing of the coach runs while iRacing is open. Analysing an .ibt afterwards on the
/// Coaching page works either way.
/// </summary>
public static class LiveCoaching
{
    private static readonly object Gate = new();
    private static IRacingConnection? _connection;
    private static LiveCoach? _coach;
    private static WindowsSpeech? _voice;
    private static bool _coachCompare;

    // Live coaching switched off keeps its coach here, so switching it back on in the same
    // session carries on with the same session and report instead of starting another.
    private static LiveCoach? _paused;
    private static bool _pausedCompare;

    /// <summary>Where each track and car's best lap ever is kept.</summary>
    public static ReferenceLapStore References { get; } = new(ReferenceLapStore.DefaultDirectory);

    /// <summary>A faster driver's lap per track and car, imported to compare against on the
    /// Coaching page; kept apart from the driver's own best ever.</summary>
    public static ReferenceLapStore Faster { get; } = new(Path.Combine(ReferenceLapStore.DefaultDirectory, "faster"));

    /// <summary>Each corner's type, kept per track and car once a session has settled it.</summary>
    public static CornerTypeStore Types { get; } = new(CornerTypeStore.DefaultDirectory);

    /// <summary>The corners pinned on the Coaching page to work on, per track and car.</summary>
    public static History.FocusStore Focus { get; } = new(History.FocusStore.DefaultDirectory);

    /// <summary>The switches in force; changed from the Coaching page.</summary>
    public static CoachingSettings Settings { get; private set; } = CoachingSettings.Load();

    /// <summary>Called once by the app with its connection; starts coaching if it is switched on.</summary>
    public static void Attach(IRacingConnection connection)
    {
        lock (Gate)
        {
            _connection = connection;
        }

        // Whatever runs after iRacing closes is another session, even on the same track and car.
        connection.Disconnected += (_, _) =>
        {
            lock (Gate)
            {
                _coach?.EndSession();
                _paused?.EndSession();
            }
        };
        Apply();
    }

    /// <summary>The app is closing: stop coaching and let the last report finish writing.</summary>
    public static void Detach()
    {
        lock (Gate)
        {
            Stop();
            _connection = null;
        }
    }

    /// <summary>What the live coach did, one line at a time (the app writes it to its log), on the reader thread.</summary>
    public static event Action<string>? Log;

    private static void Write(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            Log?.Invoke(line);
        }
    }

    /// <summary>Raised after the switches changed (from the page or a shortcut), on the caller's thread.</summary>
    public static event Action? SettingsChanged;

    /// <summary>Switches live coaching and its voice, saves the choice and applies it now.</summary>
    public static void Change(CoachingSettings settings)
    {
        Settings = settings;
        settings.Save();
        Apply();
        SettingsChanged?.Invoke();
    }

    /// <summary>
    /// Live coaching on if it was off and the other way round, for a shortcut pressed while driving;
    /// says which it is now, since the driver can't look at the screen. Returns whether it is on.
    /// </summary>
    public static bool Toggle()
    {
        // One press can arrive twice (seen 1.1 s apart on a driver's PC): a second toggle that soon
        // would switch it straight back, so it is ignored.
        var now = Environment.TickCount64;
        if (now - _lastToggle < ToggleDebounceMs)
        {
            Write("Live coaching shortcut repeated within 1.5 s: ignored.");
            return Settings.LiveEnabled;
        }

        _lastToggle = now;
        var on = !Settings.LiveEnabled;
        Change(Settings with { LiveEnabled = on });
        Announce(on ? "Live coaching on." : "Live coaching off.");
        OverlayToast.Show(CoachingText.T(on ? "hotkeys.liveCoachingOn" : "hotkeys.liveCoachingOff"));
        return on;
    }

    private const long ToggleDebounceMs = 1500;
    private static long _lastToggle = long.MinValue / 2;

    private static WindowsSpeech? _announcer;

    private static void Announce(string text)
    {
        try
        {
            lock (Gate)
            {
                (_announcer ??= new WindowsSpeech()).Say(text);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            // No voice installed: the toast still says it.
        }
    }

    private static void Apply()
    {
        lock (Gate)
        {
            var on = Settings.LiveEnabled && _connection is not null;
            if (!on)
            {
                Stop(keep: true);
                return;
            }

            // A different reference starts a new session (and report) against it.
            if (_coach is not null && _coachCompare != Settings.CompareWithBestEver)
            {
                Stop();
            }

            if (_coach is null && _paused is not null && _pausedCompare == Settings.CompareWithBestEver)
            {
                _coach = _paused;
                _coachCompare = _pausedCompare;
                _paused = null;
                Write("Live coaching resumed.");
                if (_connection!.Session is { } info)
                {
                    _coach.OnSessionInfo(info);
                }

                _connection.SessionInfoUpdated += OnSessionInfo;
                _connection.TelemetryUpdated += OnTelemetry;
            }

            _paused = null;
            if (_coach is null)
            {
                _coachCompare = Settings.CompareWithBestEver;
                _coach = new LiveCoach(new CornerMapStore(CornerMapStore.DefaultDirectory), new SessionReportStore(SessionReportStore.DefaultDirectory), TrackTurnNames.Default, References, Types, Focus)
                {
                    CompareWithBestEver = _coachCompare,
                };
                _coach.Log += Write;
                Write(_connection!.Session?.WeekendInfo?.TrackName is { } track ? $"Live coaching on, at {track}." : "Live coaching on; waiting for iRacing to name the track.");
                _coach.Notice += (cue, isFixed) => OverlayToast.Show(isFixed
                    ? CoachingText.F("coaching.cue.fixed", cue.CornerName)
                    : $"{cue.CornerName}: {CoachingText.TryT("coaching.cue.word." + cue.Kind) ?? cue.Words}");
                if (_connection!.Session is { } info)
                {
                    _coach.OnSessionInfo(info);
                }

                _connection.SessionInfoUpdated += OnSessionInfo;
                _connection.TelemetryUpdated += OnTelemetry;
            }

            if (Settings.SpeakDebrief || Settings.Cues == Advice.CueMode.Voice)
            {
                _voice ??= new WindowsSpeech();
            }
            else
            {
                _voice?.Dispose();
                _voice = null;
            }

            var logged = _voice is null ? null : new LoggedVoice(_voice, Write);
            _coach.Voice = Settings.SpeakDebrief ? logged : null;
            _coach.Cues = Settings.Cues;
            _coach.CueVoice = Settings.Cues == Advice.CueMode.Voice ? logged : null;
            Write(Settings.SpeakDebrief ? "Spoken debrief on." : "Spoken debrief off: the coach records without speaking.");
        }
    }

    // Under Gate. With keep, the coach is set aside to carry on if switched back on.
    private static void Stop(bool keep = false)
    {
        if (_connection is { } c)
        {
            c.SessionInfoUpdated -= OnSessionInfo;
            c.TelemetryUpdated -= OnTelemetry;
        }

        _coach?.Flush();
        if (_coach is not null)
        {
            Write(keep ? "Live coaching off (kept to carry on)." : "Live coaching stopped.");
        }

        if (keep && _coach is not null)
        {
            _paused = _coach;
            _pausedCompare = _coachCompare;
        }
        else if (!keep)
        {
            _paused = null;
        }

        _coach = null;
        _voice?.Dispose();
        _voice = null;
    }

    private static void OnSessionInfo(object? sender, IracingSessionInfo info)
    {
        lock (Gate)
        {
            _coach?.OnSessionInfo(info);
        }
    }

    private static void OnTelemetry(object? sender, TelemetrySnapshot snapshot)
    {
        lock (Gate)
        {
            _coach?.OnTelemetry(snapshot);
        }
    }
}
