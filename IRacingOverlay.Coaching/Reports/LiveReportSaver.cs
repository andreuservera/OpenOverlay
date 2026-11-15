namespace IRacingOverlay.Coaching.Reports;

/// <summary>
/// Keeps a live session's report saved while the driver drives: the report is built on the caller's
/// thread (the session isn't thread-safe, and building it takes a few milliseconds) and written to
/// disk on a background thread, so the telemetry reader never waits on the disk. A save that fails
/// is dropped; the next lap saves again.
/// </summary>
public sealed class LiveReportSaver
{
    private readonly SessionReportStore _store;
    private readonly LapTraceStore _traces;
    private readonly HashSet<(string, int)> _tracesQueued = [];
    private readonly object _gate = new();
    private Task _writing = Task.CompletedTask;

    public LiveReportSaver(SessionReportStore store)
    {
        _store = store;
        _traces = LapTraceStore.For(store);
    }

    /// <summary>Where the last report was written; null before the first.</summary>
    public string? LastPath { get; private set; }

    /// <summary>The last save's error, if it failed.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>Builds the report now and writes it in the background, after any write still under way.</summary>
    public void Save(CoachingSession session, SessionReportInfo info)
    {
        var report = SessionReportBuilder.Build(session, info);
        // Each measured lap's channels once, for comparing laps later (copied here: the session isn't thread-safe).
        var laps = session.AllAnalyses.Select(a => a.Lap)
            .Where(l => l.Trace is not null && _tracesQueued.Add((info.Id, l.LapNumber)))
            .Select(l => StoredLap.From(l.LapNumber, l.LapTime, l.Trace!))
            .ToList();
        lock (_gate)
        {
            _writing = _writing.ContinueWith(_ => Write(report, laps), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>Waits for the writes under way, at most <paramref name="timeout"/> (5 s by default).</summary>
    public void Flush(TimeSpan? timeout = null)
    {
        Task writing;
        lock (_gate)
        {
            writing = _writing;
        }

        writing.Wait(timeout ?? TimeSpan.FromSeconds(5));
    }

    private void Write(SessionReport report, IReadOnlyList<StoredLap> laps)
    {
        try
        {
            foreach (var lap in laps)
            {
                _traces.Save(report.Id, lap);
            }

            _store.Save(report);
            LastPath = _store.PathFor(report.Id);
            LastError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LastError = e;
        }
    }
}
