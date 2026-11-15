using IRacingOverlay.Coaching.Reports;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public sealed class LapTraceStoreTests : IDisposable
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coach-traces-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static CoachingSession Drive(params double[][] laps)
    {
        var session = new CoachingSession(new CoachingOptions { CornerMapLaps = 3 });
        foreach (var f in Track.Drive(laps))
        {
            session.Observe(f);
        }

        session.Flush();
        return session;
    }

    [Fact]
    public void EveryMeasuredLapIsKeptAndReadsBackTheSame()
    {
        var normal = Track.SpeedProfile();
        var session = Drive(normal, normal, normal, Track.SpeedProfile(new Dictionary<int, double> { [1] = 0.9 }));
        var store = new LapTraceStore(_dir);

        store.SaveAll("s1", session);

        var laps = store.Laps("s1");
        Assert.Equal(session.AllAnalyses.Select(a => a.Lap.LapNumber), laps);
        var lap = session.AllAnalyses[0].Lap;
        var back = store.Load("s1", lap.LapNumber)!;
        Assert.Equal(lap.Trace!.Length, back.Length);
        Assert.Equal(lap.Trace.Speed[300] * 3.6f, back.SpeedKmh[300], 3);
        Assert.Equal(lap.Trace.Time[^1], back.Time[^1]);
        Assert.Equal(lap.LapTime, back.LapTime);
        Assert.Null(store.Load("s1", 99));

        store.Delete("s1");
        Assert.Empty(store.Laps("s1"));
    }

    [Fact]
    public void TheDeltaSaysWhereTheSlowerLapLosesItsTime()
    {
        var normal = Track.SpeedProfile();
        var session = Drive(normal, normal, normal, Track.SpeedProfile(new Dictionary<int, double> { [1] = 0.9 }));
        var laps = session.AllAnalyses.Select(a => StoredLap.From(a.Lap.LapNumber, a.Lap.LapTime, a.Lap.Trace!)).ToList();
        var fast = laps[1];
        var slow = laps[^1];

        var whole = LapDelta.Between(fast, slow, 0, fast.Length - 1);
        Assert.Equal(slow.LapTime - fast.LapTime, whole[^1], 2);
        Assert.Equal(0, whole[0]);

        // Over a stretch, the delta starts at zero.
        var part = LapDelta.Between(fast, slow, 1000, 1200);
        Assert.Equal(0, part[0]);
        Assert.Equal(201, part.Length);
    }

    [Fact]
    public void ALiveSessionKeepsItsLapsAsTheyAreDriven()
    {
        var reports = new SessionReportStore(_dir);
        var saver = new LiveReportSaver(reports);
        var normal = Track.SpeedProfile();
        var session = Drive(normal, normal, normal);

        saver.Save(session, new SessionReportInfo { Id = "live-1", Source = "live", StartedUtc = DateTime.UtcNow });
        saver.Save(session, new SessionReportInfo { Id = "live-1", Source = "live", StartedUtc = DateTime.UtcNow });
        saver.Flush();

        Assert.Equal(session.AllAnalyses.Count, LapTraceStore.For(reports).Laps("live-1").Count);
        Assert.Single(reports.LoadAll());
    }
}
