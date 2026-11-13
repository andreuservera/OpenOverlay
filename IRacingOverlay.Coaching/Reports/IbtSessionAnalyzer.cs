using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Ibt;

namespace IRacingOverlay.Coaching.Reports;

/// <summary>
/// Runs the coach over a whole .ibt telemetry file and builds its session report, the same way the
/// CLI does: the saved corner map for the track when there is one, otherwise corners agreed from up
/// to five of the fastest clean laps of the file (and saved, when a store is given).
/// </summary>
public static class IbtSessionAnalyzer
{
    public static (CoachingSession Session, SessionReport Report) Analyze(
        string path,
        CornerMapStore? store,
        bool relearn = false,
        bool saveMap = true,
        CancellationToken cancel = default,
        References.ReferenceLapStore? references = null)
    {
        using var ibt = IbtFile.Open(path);
        var weekend = ibt.Session?.WeekendInfo;
        var driverInfo = ibt.Session?.DriverInfo;
        var car = driverInfo?.Drivers.FirstOrDefault(d => d.CarIdx == driverInfo.DriverCarIdx)?.CarScreenName;
        var trackKey = weekend?.TrackName;

        var session = new CoachingSession(new CoachingOptions
        {
            TrackKey = trackKey,
            Store = trackKey is null ? null : store,
            LoadSavedMap = !relearn,
            TurnNames = TrackTurnNames.Default,
            CornerMapLaps = int.MaxValue, // agreed below, from the fastest laps of the whole file
        });

        var n = 0;
        foreach (var frame in ibt.Frames())
        {
            if ((++n & 0xFFF) == 0)
            {
                cancel.ThrowIfCancellationRequested();
            }

            session.Observe(frame);
        }

        session.Flush();
        if (session.CornerMapOrigin != CornerMapOrigin.Saved && session.Analyses.Count > 0)
        {
            var fastest = session.Analyses.Select(a => a.Lap).OrderBy(l => l.LapTime).Take(5).ToList();
            session.RebuildCornerMap(fastest, save: saveMap && store is not null);
        }

        var sessions = ibt.Session?.SessionInfo?.Sessions;
        var sessionType = sessions is { Count: > 0 } && session.Laps.Count > 0
            ? sessions.FirstOrDefault(s => s.SessionNum == session.Laps[^1].SessionNum)?.SessionType
            : null;

        var report = SessionReportBuilder.Build(session, new SessionReportInfo
        {
            Id = "ibt-" + SessionReportStore.Slug(Path.GetFileNameWithoutExtension(path)),
            Source = "ibt",
            SourceFile = Path.GetFullPath(path),
            StartedUtc = ibt.SessionStartUtc,
            TrackKey = trackKey,
            TrackName = weekend?.TrackDisplayName ?? trackKey,
            CarName = car,
            SessionType = string.IsNullOrWhiteSpace(sessionType) ? null : sessionType,
        });
        // The file's best lap becomes the track and car's best ever if it is quicker than the stored one.
        if (references is not null && trackKey is not null && car is not null && session.BestLap is { } best)
        {
            references.Offer(trackKey, car, best, "ibt", Path.GetFullPath(path), ibt.SessionStartUtc);
        }

        return (session, report);
    }
}
