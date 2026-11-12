using IRacingOverlay.Coaching.Ibt;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public class CoachingSessionTests
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();

    [Fact]
    public void LearnsCornersFromTheFirstCleanLapAndKeepsTheBestPassOfEach()
    {
        var normal = Track.SpeedProfile();
        var slowT2 = Track.SpeedProfile(new Dictionary<int, double> { [3] = 0.92 });
        var slowT1 = Track.SpeedProfile(new Dictionary<int, double> { [1] = 0.9 });
        // lap 0 partial, lap 1 normal, lap 2 slow in T2, lap 3 slow in T1
        var frames = Track.Drive([normal, normal, slowT2, slowT1]);

        var session = new CoachingSession();
        var analyzed = 0;
        session.LapAnalyzed += _ => analyzed++;
        foreach (var f in frames)
        {
            session.Observe(f);
        }

        session.Flush();

        Assert.Equal(3, analyzed);
        Assert.NotNull(session.CornerMap);
        Assert.Equal(1, session.CornerMapSource!.LapNumber);
        Assert.Equal(3, session.CornerMap!.Corners.Count);

        // The slow corners never hold the best pass; lap 1's passes are (within float noise) the best.
        Assert.NotEqual(3, session.BestPasses["T1"].LapNumber);
        Assert.NotEqual(2, session.BestPasses["T2"].LapNumber);
        var lap1 = session.Analyses.Single(a => a.Lap.LapNumber == 1);
        Assert.All(lap1.LossToBestSeconds.Values, loss => Assert.InRange(loss, 0, 0.002));

        var lap2 = session.Analyses.Single(a => a.Lap.LapNumber == 2);
        Assert.True(lap2.LossToBestSeconds["T2"] > 0.1);
        Assert.Equal(0, lap2.LossToBestSeconds["T1"], 0.005);

        var lap3 = session.Analyses.Single(a => a.Lap.LapNumber == 3);
        Assert.True(lap3.LossToBestSeconds["T1"] > 0.1);

        Assert.Equal(TrackSimulator.LapTime(normal), session.BestLap!.LapTime, 0.002);
        Assert.True(session.TheoreticalBestLapTime <= session.BestLap.LapTime + 1e-9);
    }

    [Fact]
    public void RunsTheSameOverAnIbtFile()
    {
        var normal = Track.SpeedProfile();
        var frames = Track.Drive([normal, normal, normal]);
        using var ibt = IbtFile.Open(new MemoryStream(IbtWriter.Write(frames)));

        var session = new CoachingSession();
        foreach (var f in ibt.Frames())
        {
            session.Observe(f);
        }

        Assert.Equal(2, session.Analyses.Count);
        Assert.Equal(3, session.CornerMap!.Corners.Count);
        Assert.Equal(TrackSimulator.LapTime(normal), session.BestLap!.LapTime, 0.002);
    }

    [Fact]
    public void RebuildingTheCornerMapRemeasuresEveryLap()
    {
        var normal = Track.SpeedProfile();
        var session = new CoachingSession();
        foreach (var f in Track.Drive([normal, normal, normal]))
        {
            session.Observe(f);
        }

        var best = session.BestLap!;
        session.RebuildCornerMap(best);

        Assert.Same(best, session.CornerMapSource);
        Assert.Equal(2, session.Analyses.Count);
        Assert.All(session.Analyses, a => Assert.Equal(3, a.Passes.Count));
    }
}
