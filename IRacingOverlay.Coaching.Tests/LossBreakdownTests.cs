using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Reports;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public class LossBreakdownTests
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();

    private static CoachingSession Drive(IReadOnlyList<double[]> laps)
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
    public void ThePhasesAddUpToTheCornersLossAndPointAtTheSlowCorner()
    {
        var normal = Track.SpeedProfile();
        var slowT2 = Track.SpeedProfile(new Dictionary<int, double> { [3] = 0.9 });
        var session = Drive([normal, normal, slowT2]);
        var bestLap = session.Analyses.Single(a => a.Lap.LapNumber == 1);
        var slowLap = session.Analyses.Single(a => a.Lap.LapNumber == 2);
        var best = bestLap.Passes[1];
        var slow = slowLap.Passes[1];

        var split = LossBreakdown.Split(slowLap.Lap.Trace!, bestLap.Lap.Trace!, best);

        Assert.Equal(slow.ScoreTime - best.ScoreTime, split.Total, 0.002);
        Assert.True(split.Total > 0.1);
        // Slower through the corner: the time goes between the brake and the throttle, not on the approach.
        Assert.True(split.Rotation > split.Approach);

        // A corner driven the same both times loses nothing in any phase.
        var same = LossBreakdown.Split(slowLap.Lap.Trace!, bestLap.Lap.Trace!, bestLap.Passes[0]);
        Assert.InRange(Math.Abs(same.Total), 0, 0.005);
    }

    [Fact]
    public void GripUsageIsMeasuredAgainstWhatTheCarShowedItCanDo()
    {
        var normal = Track.SpeedProfile();
        var slowT2 = Track.SpeedProfile(new Dictionary<int, double> { [3] = 0.9 });
        var session = Drive([normal, normal, normal, slowT2]);
        var traces = session.Analyses.Select(a => a.Lap.Trace!).ToList();

        var grip = GripEnvelope.Learn(traces);

        Assert.NotNull(grip);
        var apex = session.CornerMap!.Corners[1].ApexM;
        var good = grip!.Usage(session.Analyses[0].Lap.Trace!, apex - 20, apex + 20);
        var slow = grip.Usage(session.Analyses.Single(a => a.Lap.LapNumber == 3).Lap.Trace!, apex - 20, apex + 20);
        Assert.InRange(good!.Value, 0.85, 1.1);
        Assert.True(slow < good - 0.1);
    }

    [Fact]
    public void TheMeasurementThatMovesTheTimeIsFound()
    {
        var corner = new Corner("T1", 100, 200, 300);
        var passes = Enumerable.Range(0, 8).Select(i => new CornerPass
        {
            Corner = corner,
            LapNumber = i + 1,
            SegmentTime = 10 - 0.05 * i,
            EntrySpeed = 50,
            BrakePointM = 110 + (i % 3),
            MinSpeed = 20 + i * 0.3f,
            MinSpeedAtM = 200,
            ExitSpeed = 40,
            ExitSpeedPlus100 = 45 + (i % 2),
            GearAtApex = 2,
        }).ToList();

        var d = CornerStats.DecidedBy(passes);

        Assert.Equal("minimum speed", d!.Value.Measure);
        Assert.True(d.Value.R < -0.9);
        Assert.Null(CornerStats.DecidedBy(passes.Take(4).ToList()));
    }

    [Fact]
    public void TheReportSaysWhereTheTimeGoes()
    {
        var normal = Track.SpeedProfile();
        var slowHairpin = Track.SpeedProfile(new Dictionary<int, double> { [1] = 0.9 });
        var session = Drive([normal, normal, slowHairpin, slowHairpin, slowHairpin, slowHairpin, slowHairpin]);

        var report = SessionReportBuilder.Build(session, new SessionReportInfo { Id = "t", Source = "ibt" });

        var hairpin = report.Corners[0];
        Assert.Equal(5, hairpin.LossByPhase.Count);
        Assert.Equal(hairpin.MedianLoss!.Value, hairpin.LossByPhase.Sum(p => p.Seconds), 0.01);
        Assert.NotNull(hairpin.Grip);
        Assert.NotNull(hairpin.TimeSpread);
        Assert.Contains(SessionSummaryText.Headline(report), l => l.StartsWith("Most of it goes", StringComparison.Ordinal));
    }
}
