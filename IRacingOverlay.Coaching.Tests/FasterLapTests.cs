using IRacingOverlay.Coaching.References;
using IRacingOverlay.Coaching.Reports;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public sealed class FasterLapTests : IDisposable
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coach-faster-" + Guid.NewGuid().ToString("N"));

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
    public void AFasterLapTellsTechniqueFromConsistency()
    {
        var normal = Track.SpeedProfile();
        // Corner 3 is sometimes slow (a consistency problem); the best passes are all the same.
        var off3 = Track.SpeedProfile(new Dictionary<int, double> { [3] = 0.88 });
        var mine = Drive(normal, normal, normal, off3, off3, normal, off3);
        var report = SessionReportBuilder.Build(mine, new SessionReportInfo { Id = "s", Source = "test", StartedUtc = DateTime.UtcNow });

        // The faster driver is quicker through corner 1 only.
        var theirs = Drive(normal, normal, Track.SpeedProfile(new Dictionary<int, double> { [1] = 1.12 }));
        var lap = theirs.Analyses.Select(a => a.Lap).MinBy(l => l.LapTime)!;
        var faster = new ReferenceLap { TrackKey = "t", Car = "c", LapTime = lap.LapTime, RecordedUtc = DateTime.UtcNow, Source = "faster", Trace = lap.Trace! };

        var gaps = FasterLapComparison.Compare(report, faster);

        Assert.Equal(report.Corners.Count, gaps.Count);
        var technique = gaps.Where(g => g.Verdict == GapVerdict.Technique).ToList();
        var consistency = gaps.Where(g => g.Verdict == GapVerdict.Consistency).ToList();
        Assert.Single(technique);
        Assert.Single(consistency);
        Assert.NotEqual(technique[0].CornerId, consistency[0].CornerId);
        Assert.Contains(technique[0].Differences, d => d.Measure == "minSpeed" && d.Amount > 0 && d.SetupSensitive);
    }

    [Fact]
    public void AnotherLayoutIsNotCompared()
    {
        var normal = Track.SpeedProfile();
        var mine = Drive(normal, normal, normal);
        var report = SessionReportBuilder.Build(mine, new SessionReportInfo { Id = "s", Source = "test", StartedUtc = DateTime.UtcNow }) with { TrackLengthMetres = 9000 };
        var lap = mine.BestLap!;
        var faster = new ReferenceLap { TrackKey = "t", Car = "c", LapTime = lap.LapTime, RecordedUtc = DateTime.UtcNow, Source = "faster", Trace = lap.Trace! };

        Assert.False(FasterLapComparison.SameLayout(report, faster.Trace));
        Assert.Empty(FasterLapComparison.Compare(report, faster));
    }

    [Fact]
    public void AnExportedLapReadsBack()
    {
        var store = new ReferenceLapStore(_dir);
        var normal = Track.SpeedProfile();
        var session = Drive(normal, normal, normal);
        store.Offer("spa", "GT3", session.BestLap!, "live");
        var file = Path.Combine(_dir, "mine" + ReferenceLapStore.FileExtension);

        store.Export("spa", "GT3", file);
        var back = ReferenceLapStore.ReadFile(file)!;

        Assert.Equal(session.BestLap!.LapTime, back.LapTime, 6);
        Assert.Equal("GT3", back.Car);
        Assert.Null(ReferenceLapStore.ReadFile(Path.Combine(_dir, "none.oolap")));
        Assert.Throws<FileNotFoundException>(() => store.Export("spa", "MX-5", file));
    }
}
