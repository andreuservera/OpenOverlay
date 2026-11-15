using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.TrackMaps;

/// <summary>
/// Keeps the current track's shape: loads it when the track changes, and while there is none yet (or
/// only a rough one) learns it from the laps being driven. Fed on the telemetry thread, read on the
/// UI thread; only runs while something shows the circuit map (<see cref="Wanted"/>).
/// </summary>
internal sealed class TrackOutlineLearner
{
    /// <summary>A shape that closes this well isn't worth relearning.</summary>
    public const double GoodEnoughClosure = 0.02;

    private readonly TrackOutlineStore _store;
    private readonly TrackOutlineRecorder _recorder = new();
    private string? _track;
    private TrackOutline? _outline;
    private volatile bool _loaded;
    private volatile bool _wanted;

    public TrackOutlineLearner(TrackOutlineStore? store = null) => _store = store ?? new TrackOutlineStore();

    /// <summary>Set from the UI thread: true while a circuit map is on screen.</summary>
    public bool Wanted
    {
        get => _wanted;
        set => _wanted = value;
    }

    /// <summary>The shape of the track being driven, once known.</summary>
    public TrackOutline? Current => Volatile.Read(ref _outline);

    /// <summary>How far round the lap being learned the car is, while there is no shape yet.</summary>
    public double? LearningProgress => Current is null ? _recorder.Progress : null;

    /// <summary>Called for every tick, on the telemetry thread.</summary>
    public void Observe(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (!_wanted || session?.WeekendInfo?.TrackName is not { Length: > 0 } track)
        {
            return;
        }

        if (track != _track)
        {
            SwitchTo(track);
            return;
        }

        if (!_loaded || Current is { ClosureShare: <= GoodEnoughClosure })
        {
            return;
        }

        if (!telemetry.HasVariable(TelemetryVarNames.LapDistPct) || !telemetry.HasVariable(TelemetryVarNames.Speed) ||
            !telemetry.HasVariable(TelemetryVarNames.YawNorth) || !telemetry.HasVariable(TelemetryVarNames.SessionTime))
        {
            return;
        }

        var sample = new OutlineSample(
            telemetry.GetFloat(TelemetryVarNames.LapDistPct),
            telemetry.GetFloat(TelemetryVarNames.Speed),
            telemetry.GetFloat(TelemetryVarNames.YawNorth),
            telemetry.GetDouble(TelemetryVarNames.SessionTime),
            telemetry.HasVariable(TelemetryVarNames.OnPitRoad) && telemetry.GetBool(TelemetryVarNames.OnPitRoad),
            !telemetry.HasVariable(TelemetryVarNames.IsOnTrack) || telemetry.GetBool(TelemetryVarNames.IsOnTrack));

        if (_recorder.Observe(sample, track) is not { } learned)
        {
            return;
        }

        var previous = Current;
        if (previous is not null && learned.ClosureShare >= previous.ClosureShare)
        {
            return;
        }

        Volatile.Write(ref _outline, learned);
        AppLog.Info("TrackMap", previous is null ? "Track shape learned" : "Track shape improved", new Dictionary<string, string>
        {
            ["track"] = track,
            ["lengthM"] = learned.LengthM.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
            ["closure"] = learned.ClosureShare.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
        });
        Task.Run(() =>
        {
            try
            {
                _store.Save(learned);
            }
            catch (Exception e)
            {
                AppLog.Warn("TrackMap", "Could not save the track shape", e);
            }
        });
    }

    private void SwitchTo(string track)
    {
        _track = track;
        _loaded = false;
        Volatile.Write(ref _outline, null);
        _recorder.Reset();
        Task.Run(() =>
        {
            var saved = _store.Load(track);
            // Still the same track by the time the file is read.
            if (Volatile.Read(ref _track) == track)
            {
                Volatile.Write(ref _outline, saved);
                _loaded = true;
            }
        });
    }
}
