using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Telemetry;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public class CornerValidityTests
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();

    // Track.Drive: lap k is driven with profile k, lap 0 partial.
    // Standard track corners: T1 hairpin (piece 1), T2 medium (piece 3), T3 tight (piece 7).
    private static List<TelemetryFrame> MarkOff(List<TelemetryFrame> frames, int lap, int fromM, int toM) =>
        frames.Select(f => f.Lap == lap && f.LapDist >= fromM && f.LapDist <= toM ? f with { TrackSurface = TrackSurface.OffTrack } : f).ToList();

    private static CoachingSession Run(IEnumerable<TelemetryFrame> frames, CoachingOptions? options = null)
    {
        var session = new CoachingSession(options);
        foreach (var f in frames)
        {
            session.Observe(f);
        }

        session.Flush();
        return session;
    }

    [Fact]
    public void AnOffTrackOnlyVoidsTheCornerItTouched()
    {
        var normal = Track.SpeedProfile();
        var fasterT1 = Track.SpeedProfile(new Dictionary<int, double> { [1] = 1.04 });
        var t2 = Track.PieceStart(3);
        var frames = MarkOff(Track.Drive([normal, normal, fasterT1, normal]), lap: 2, t2 + 40, t2 + 70);

        var session = Run(frames);

        // Lap 2 doesn't count as a lap…
        var lap2 = session.AllAnalyses.Single(a => a.Lap.LapNumber == 2);
        Assert.False(lap2.IsCleanLap);
        Assert.DoesNotContain(session.Analyses, a => a.Lap.LapNumber == 2);
        Assert.NotEqual(2, session.BestLap!.LapNumber);

        // …but its T1, the best ever, does; only T2 is voided.
        Assert.True(lap2.Passes.Single(p => p.Corner.Name == "T1").IsClean);
        Assert.True(lap2.Passes.Single(p => p.Corner.Name == "T2").Flaws.HasFlag(CornerPassFlaw.OffTrack));
        Assert.True(lap2.Passes.Single(p => p.Corner.Name == "T3").IsClean);
        Assert.Equal(2, session.BestPasses["T1"].LapNumber);
        Assert.NotEqual(2, session.BestPasses["T2"].LapNumber);
        Assert.False(lap2.LossToBestSeconds.ContainsKey("T2"));

        // The theoretical best uses the faster T1 from the lap that didn't count.
        Assert.True(session.TheoreticalBestLapTime < session.BestLap.LapTime - 0.05);
    }

    [Fact]
    public void FlawedLapsAreAnnouncedAndMeasuredOnceTheCornersAreKnown()
    {
        var normal = Track.SpeedProfile();
        var t3 = Track.PieceStart(7);
        // Lap 1 is off at T3 before any clean lap has given a corner map.
        var frames = MarkOff(Track.Drive([normal, normal, normal, normal]), lap: 1, t3 + 30, t3 + 50);

        var session = new CoachingSession();
        var flawed = new List<LapAnalysis>();
        session.FlawedLapAnalyzed += flawed.Add;
        foreach (var f in frames)
        {
            session.Observe(f);
        }

        Assert.Empty(flawed); // no map yet when lap 1 finished
        var lap1 = session.AllAnalyses.Single(a => a.Lap.LapNumber == 1);
        Assert.Equal(2, lap1.Passes.Count(p => p.IsClean));
        Assert.Equal(2, session.Analyses.Count);
    }

    [Fact]
    public void AnIncidentVoidsTheCornerBeforeIt()
    {
        var normal = Track.SpeedProfile();
        var t1End = Track.PieceStart(2) + 40;
        var frames = Track.Drive([normal, normal, normal])
            .Select(f => f.Lap == 2 && f.LapDist >= t1End || f.Lap > 2 ? f with { IncidentCount = 1 } : f).ToList();

        var lap2 = Run(frames).AllAnalyses.Single(a => a.Lap.LapNumber == 2);

        Assert.False(lap2.IsCleanLap);
        Assert.True(lap2.Passes.Single(p => p.Corner.Name == "T1").Flaws.HasFlag(CornerPassFlaw.Incident));
        Assert.True(lap2.Passes.Single(p => p.Corner.Name == "T3").IsClean);
    }

    [Fact]
    public void TheCornerRightAfterAnOffIsVoidedToo()
    {
        var lap = new LapTrace(1000);
        for (var i = 0; i < lap.Length; i++)
        {
            lap.Speed[i] = 40;
            lap.Time[i] = i / 40f;
        }

        for (var i = 300; i < 320; i++)
        {
            lap.OffTrack[i] = true;
        }

        Assert.Equal(CornerPassFlaw.AfterOffTrack, CornerChecks.Inspect(lap, 450, 700));
        Assert.Equal(CornerPassFlaw.None, CornerChecks.Inspect(lap, 650, 900));
        Assert.Equal(CornerPassFlaw.OffTrack, CornerChecks.Inspect(lap, 100, 310) & CornerPassFlaw.OffTrack);
    }

    [Fact]
    public void StartLapsVoidTheirFirstCorners()
    {
        var normal = Track.SpeedProfile();
        var session = Run(Track.Drive([normal, normal]));
        var trace = session.Analyses[0].Lap.Trace!;

        var passes = CornerAnalyzer.Analyze(trace, session.CornerMap!, 1, LapFlaw.StartLap);

        Assert.All(passes.Take(2), p => Assert.True(p.Flaws.HasFlag(CornerPassFlaw.StartLap)));
        Assert.True(passes[2].IsClean);
    }

    [Fact]
    public void CornersAreScoredToTheNextCornerOrFourHundredMetresPastTheirEnd()
    {
        var normal = Track.SpeedProfile();
        var session = Run(Track.Drive([normal, normal]));
        var map = session.CornerMap!;
        var a = session.Analyses[0];

        for (var i = 0; i < a.Passes.Count; i++)
        {
            var p = a.Passes[i];
            var straightEnd = i + 1 < map.Corners.Count ? map.Corners[i + 1].StartM : a.Lap.Trace!.Length - 1;
            Assert.Equal(Math.Min(straightEnd, p.Corner.EndM + CornerAnalyzer.MaxStraightMetres), p.ScoreEndM);
            Assert.True(p.RestOfStraightClean);
            Assert.True(p.ScoreTime > p.SegmentTime);
        }

        // Corner scores plus the run to the first corner make up the lap.
        Assert.Equal(a.Lap.LapTime, a.OutsideCornersTime!.Value + a.Passes.Sum(p => p.ScoreTime + p.RestOfStraightTime), 0.01);
        Assert.Equal(a.Lap.LapTime, session.TheoreticalBestLapTime!.Value, 0.01);
    }

    [Fact]
    public void ASimulatedLapFollowsTheTrackSoItsPathRatioIsOne()
    {
        var normal = Track.SpeedProfile();
        var session = Run(Track.Drive([normal, normal]));

        Assert.All(session.Analyses[0].Passes, p => Assert.InRange(p.PathRatio, 0.99, 1.01));
    }

    [Fact]
    public void AClearlyShorterPathIsACut()
    {
        var corner = new Corner("T1", 100, 200, 300);
        CornerPass P(double ratio) => new()
        {
            Corner = corner,
            LapNumber = 1,
            SegmentTime = 10,
            EntrySpeed = 50,
            MinSpeed = 20,
            MinSpeedAtM = 200,
            ExitSpeed = 40,
            ExitSpeedPlus100 = 45,
            GearAtApex = 2,
            PathRatio = ratio,
        };

        var usual = new[] { P(0.97), P(0.975), P(0.98) };

        Assert.True(CornerChecks.IsShortcut(P(0.93), usual));
        Assert.False(CornerChecks.IsShortcut(P(0.965), usual)); // a tighter line, not a cut
        Assert.False(CornerChecks.IsShortcut(P(0.80), usual.Take(2))); // too few passes to judge
    }

    [Fact]
    public void AFreakPassOnlyBecomesTheBestOnceAnotherComesClose()
    {
        var corner = new Corner("T1", 100, 200, 300);
        CornerPass P(double time, int lap) => new()
        {
            Corner = corner,
            LapNumber = lap,
            SegmentTime = time,
            EntrySpeed = 50,
            MinSpeed = 20,
            MinSpeedAtM = 200,
            ExitSpeed = 40,
            ExitSpeedPlus100 = 45,
            GearAtApex = 2,
        };

        var passes = new List<CornerPass> { P(10.0, 1), P(10.1, 2), P(10.2, 3), P(9.0, 4) };
        Assert.Equal(1, BestPasses.Best(passes)!.LapNumber);

        passes.Add(P(9.05, 5));
        Assert.Equal(4, BestPasses.Best(passes)!.LapNumber);

        // An ordinary improvement is taken at once.
        Assert.Equal(6, BestPasses.Best([P(10.0, 1), P(10.1, 2), P(10.2, 3), P(9.8, 6)])!.LapNumber);
    }

    [Fact]
    public void ACutPassIsNeverTheBest()
    {
        var corner = new Corner("T1", 100, 200, 300);
        var clean = new CornerPass { Corner = corner, LapNumber = 1, SegmentTime = 10, EntrySpeed = 50, MinSpeed = 20, MinSpeedAtM = 200, ExitSpeed = 40, ExitSpeedPlus100 = 45, GearAtApex = 2 };
        var cut = clean with { LapNumber = 2, SegmentTime = 9.5, Flaws = CornerPassFlaw.Shortcut };

        Assert.Equal(1, BestPasses.Select([clean, cut])["T1"].LapNumber);
    }

    [Fact]
    public void TheDebriefSaysWhatTheBestCornersAddUpTo()
    {
        var d = new Advice.LapDebrief(4, 100.5, 0.5, false, []) { TheoreticalBestLapTime = 99.2 };

        Assert.Contains("  Your best corners add up to 1:39.200.", Advice.DebriefText.Lines(d));
    }

    [Fact]
    public void TheFirstCornersAfterLeavingPitRoadAreVoided()
    {
        var normal = Track.SpeedProfile();
        // Lap 2 starts in the pit lane, which ends just before T1.
        var frames = Track.Drive([normal, normal, normal, normal])
            .Select(f => f.Lap == 2 && f.LapDist < 100 ? f with { OnPitRoad = true } : f).ToList();

        var lap2 = Run(frames).AllAnalyses.Single(a => a.Lap.LapNumber == 2);

        Assert.True(lap2.Passes[0].Flaws.HasFlag(CornerPassFlaw.PitExit));
        Assert.True(lap2.Passes[1].Flaws.HasFlag(CornerPassFlaw.PitExit));
        Assert.True(lap2.Passes[2].IsClean);
    }

    [Fact]
    public void AnOffOnTheWayIntoACornerVoidsTheCornerBefore()
    {
        var normal = Track.SpeedProfile();
        var session = Run(Track.Drive([normal, normal]));
        var map = session.CornerMap!;
        var trace = session.Analyses[0].Lap.Trace!;
        var t2 = map.Corners[1];
        for (var i = t2.StartM + 10; i < t2.StartM + 30; i++)
        {
            trace.OffTrack[i] = true;
        }

        var passes = CornerAnalyzer.Analyze(trace, map, 1);

        Assert.True(passes[0].Flaws.HasFlag(CornerPassFlaw.OffTrackNext));
        Assert.True(passes[1].Flaws.HasFlag(CornerPassFlaw.OffTrack));
        Assert.True(passes[2].IsClean);
    }

    [Fact]
    public void AnOffFarDownALongStraightOnlyVoidsThatStretch()
    {
        var normal = Track.SpeedProfile();
        var session = Run(Track.Drive([normal, normal]));
        var map = session.CornerMap!;
        var trace = session.Analyses[0].Lap.Trace!;
        // The straight after T3 runs to the line, well over 400 m.
        var t3 = map.Corners[2];
        var far = t3.EndM + CornerAnalyzer.MaxStraightMetres + 60;
        Assert.True(far < trace.Length - 30);
        for (var i = far; i < far + 20; i++)
        {
            trace.OffTrack[i] = true;
        }

        var p = CornerAnalyzer.Analyze(trace, map, 1)[2];

        Assert.True(p.IsClean);
        Assert.False(p.RestOfStraightClean);
        Assert.True(p.RestOfStraightTime > 0);
    }
}
