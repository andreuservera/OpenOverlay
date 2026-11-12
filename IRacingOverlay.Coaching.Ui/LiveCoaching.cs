using System.Text.Json;
using IRacingOverlay.Coaching.Corners;
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

    /// <summary>The switches in force; changed from the Coaching page.</summary>
    public static CoachingSettings Settings { get; private set; } = CoachingSettings.Load();

    /// <summary>Called once by the app with its connection; starts coaching if it is switched on.</summary>
    public static void Attach(IRacingConnection connection)
    {
        lock (Gate)
        {
            _connection = connection;
        }

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

    /// <summary>Switches live coaching and its voice, saves the choice and applies it now.</summary>
    public static void Change(CoachingSettings settings)
    {
        Settings = settings;
        settings.Save();
        Apply();
    }

    private static void Apply()
    {
        lock (Gate)
        {
            var on = Settings.LiveEnabled && _connection is not null;
            if (!on)
            {
                Stop();
                return;
            }

            if (_coach is null)
            {
                _coach = new LiveCoach(new CornerMapStore(CornerMapStore.DefaultDirectory), new SessionReportStore(SessionReportStore.DefaultDirectory), TrackTurnNames.Default);
                if (_connection!.Session is { } info)
                {
                    _coach.OnSessionInfo(info);
                }

                _connection.SessionInfoUpdated += OnSessionInfo;
                _connection.TelemetryUpdated += OnTelemetry;
            }

            if (Settings.SpeakDebrief)
            {
                _voice ??= new WindowsSpeech();
            }
            else
            {
                _voice?.Dispose();
                _voice = null;
            }

            _coach.Voice = _voice;
        }
    }

    // Under Gate.
    private static void Stop()
    {
        if (_connection is { } c)
        {
            c.SessionInfoUpdated -= OnSessionInfo;
            c.TelemetryUpdated -= OnTelemetry;
        }

        _coach?.Flush();
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
