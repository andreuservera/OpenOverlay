using IRacingOverlay.Coaching.Advice;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;

namespace IRacingOverlay.Coaching.Tests;

public class AdviceTests
{
    private static readonly Corner T1 = new("T1", 100, 250, 350);
    private static readonly Corner T2 = new("T2", 600, 700, 800) { Label = "La Source" };

    // A tidy pass: brake at 120 m to 90%, turn in at 180 m with the brake trailed to 200 m,
    // 80 km/h at the apex, throttle at 260 m.
    private static CornerPass Pass(Corner c, double time, int lap = 1) => new()
    {
        Corner = c,
        LapNumber = lap,
        SegmentTime = time,
        EntrySpeed = 60,
        BrakePointM = c.StartM + 20,
        PeakBrake = 0.9f,
        BrakeRampM = 5,
        BrakeReleaseM = c.StartM + 100,
        TurnInM = c.StartM + 80,
        TrailBrakeM = 20,
        MinSpeed = 80 / 3.6f,
        MinSpeedAtM = c.ApexM,
        ThrottlePickupM = c.StartM + 160,
        FullThrottleM = c.StartM + 200,
        ExitSpeed = 30,
        ExitSpeedPlus100 = 40,
        GearAtApex = 3,
    };

    [Fact]
    public void ThePassThatIsTheBestHasNothingToSay()
    {
        var best = Pass(T1, 5.0);
        Assert.Empty(CornerDiagnoser.Diagnose(best, best));
        Assert.Empty(CornerDiagnoser.Diagnose(Pass(T1, 5.0), best)); // same numbers, another lap
    }

    [Fact]
    public void BrakingEarlierIsReportedWithTheDistance()
    {
        var best = Pass(T1, 5.0);
        var pass = Pass(T1, 5.2) with { BrakePointM = best.BrakePointM - 15 };

        var f = Assert.Single(CornerDiagnoser.Diagnose(pass, best));
        Assert.Equal(FindingKind.BrakedTooEarly, f.Kind);
        Assert.Equal(15, f.Amount);
        Assert.Equal("brake 15 metres later", DebriefText.Advice(f));
    }

    [Fact]
    public void BrakingLaterIsOnlyAMistakeWhenTheCornerSuffered()
    {
        var best = Pass(T1, 5.0);
        var later = Pass(T1, 5.1) with { BrakePointM = best.BrakePointM + 12 };
        Assert.DoesNotContain(CornerDiagnoser.Diagnose(later, best), f => f.Kind == FindingKind.BrakedTooLate);

        var overshot = later with { MinSpeed = best.MinSpeed - 2, ThrottlePickupM = best.ThrottlePickupM + 25 };
        var findings = CornerDiagnoser.Diagnose(overshot, best);
        Assert.Equal(FindingKind.BrakedTooLate, findings[0].Kind); // the cause comes first
        Assert.Contains(findings, f => f.Kind == FindingKind.LowMinimumSpeed);
        Assert.Contains(findings, f => f.Kind == FindingKind.LateThrottle);
    }

    [Fact]
    public void ASofterOrSlowerFirstHitOfTheBrakeIsFound()
    {
        var best = Pass(T1, 5.0);

        var softer = CornerDiagnoser.Diagnose(Pass(T1, 5.1) with { PeakBrake = 0.75f }, best);
        Assert.Equal(FindingKind.InitialBrakeTooSoft, softer[0].Kind);
        Assert.Equal(15, softer[0].Amount, 0.5);

        var slowRamp = CornerDiagnoser.Diagnose(Pass(T1, 5.1) with { BrakeRampM = 25 }, best);
        Assert.True(slowRamp[0].IsRamp);
    }

    [Fact]
    public void LettingGoOfTheBrakeEarlyIsOnlyReportedWhenTheApexWasSlower()
    {
        var best = Pass(T1, 5.0);
        var noTrail = Pass(T1, 5.1) with { TrailBrakeM = 0 };
        Assert.Empty(CornerDiagnoser.Diagnose(noTrail, best));

        var slower = noTrail with { MinSpeed = best.MinSpeed - 1.5f };
        var findings = CornerDiagnoser.Diagnose(slower, best);
        Assert.Equal(FindingKind.TooLittleTrailBrake, findings[0].Kind);
        Assert.Equal(FindingKind.LowMinimumSpeed, findings[1].Kind);
        Assert.Equal(5.4, findings[1].Amount, 0.1);
    }

    [Fact]
    public void ASlowExitIsReportedInKilometresPerHour()
    {
        var best = Pass(T1, 5.0);
        var f = Assert.Single(CornerDiagnoser.Diagnose(Pass(T1, 5.1) with { ExitSpeedPlus100 = 38 }, best));
        Assert.Equal(FindingKind.SlowExit, f.Kind);
        Assert.Equal("exit 7 km/h slower than your best, get on the power cleaner", DebriefText.Advice(f));
    }

    [Fact]
    public void TheDebriefNamesTheWorstCornersAboveTheNoiseFloorWorstFirst()
    {
        var bests = new Dictionary<string, CornerPass> { ["T1"] = Pass(T1, 5.0), ["T2"] = Pass(T2, 4.0) };
        var lap = Analysis(3, 62.0,
            Pass(T1, 5.03, 3) with { BrakePointM = 105 },     // 0.03 s: noise, left out
            Pass(T2, 4.36, 3) with { BrakePointM = 600 });    // 0.35 s, braked 20 m early

        var d = new PostLapCoach().Debrief(lap, bests, 61.5);

        Assert.False(d.IsBestLap);
        Assert.Equal(0.5, d.DeltaToBestSeconds, 3);
        var c = Assert.Single(d.Corners);
        Assert.Equal("T2", c.Corner.Name);
        Assert.Equal(FindingKind.BrakedTooEarly, c.Main!.Kind);
        Assert.Equal(
            "1 02.0, 5 tenths off your best. La Source, 4 tenths: brake 20 metres later.",
            DebriefText.Spoken(d));
    }

    [Fact]
    public void TheSameMistakeLapAfterLapIsCalledARepeat()
    {
        var coach = new PostLapCoach();
        var bests = new Dictionary<string, CornerPass> { ["T1"] = Pass(T1, 5.0) };
        LapDebrief? d = null;
        for (var lap = 1; lap <= 3; lap++)
        {
            d = coach.Debrief(Analysis(lap, 60.2, Pass(T1, 5.2, lap) with { BrakePointM = 100 }), bests, 60.0);
        }

        Assert.Equal(2, d!.Corners[0].RepeatCount);
        Assert.Contains("Turn 1 again, 2 tenths: brake 20 metres later.", DebriefText.Spoken(d));

        coach.Reset();
        d = coach.Debrief(Analysis(4, 60.2, Pass(T1, 5.2, 4) with { BrakePointM = 100 }), bests, 60.0);
        Assert.Equal(0, d.Corners[0].RepeatCount);
    }

    [Fact]
    public void ACornerWithNothingToFixOnlyTakesASlotLeftOver()
    {
        var corners = Enumerable.Range(1, 4).Select(i => new Corner($"T{i}", i * 1000, i * 1000 + 100, i * 1000 + 200)).ToList();
        var bests = corners.ToDictionary(c => c.Name, c => Pass(c, 5.0));
        var lap = Analysis(2, 61.0,
            Pass(corners[0], 5.50, 2),                                              // biggest loss, no cause found
            Pass(corners[1], 5.30, 2) with { BrakePointM = corners[1].StartM },     // braked 20 m early
            Pass(corners[2], 5.20, 2) with { ExitSpeedPlus100 = 37 },
            Pass(corners[3], 5.10, 2) with { ThrottlePickupM = corners[3].StartM + 190 });

        var d = new PostLapCoach().Debrief(lap, bests, 60.0);

        Assert.Equal(["T2", "T3", "T4"], d.Corners.Select(c => c.Corner.Name));
    }

    [Theory]
    [InlineData(60, -1, "Turn 5, the right hairpin")]
    [InlineData(95, 1, "Turn 5, the slow left")]
    [InlineData(140, -1, "Turn 5, the right-hander")]
    [InlineData(200, 1, "Turn 5, the fast left")]
    [InlineData(140, 0, "Turn 5")]
    public void ACornerWithoutANameIsDescribedByHowSlowItIsAndWhichWayItGoes(double kmh, int direction, string expected)
    {
        var corner = new Corner("T5", 1000, 1100, 1200);
        var advice = new CornerAdvice(corner, 0.2, [], 0)
        {
            Best = Pass(corner, 5.0) with { MinSpeed = (float)(kmh / 3.6), Direction = direction },
            Index = 4,
            CornerCount = 10,
        };

        Assert.Equal(expected, DebriefText.SpokenCorner(advice));
    }

    [Fact]
    public void TheLastCornerAndNamedCornersAreSaidAsSuch()
    {
        var last = new CornerAdvice(new Corner("T10", 6500, 6700, 6800), 0.2, [], 0) { Index = 9, CornerCount = 10 };
        Assert.Equal("The last corner again", DebriefText.SpokenCorner(last, again: true));

        var named = last with { Corner = last.Corner with { Label = "Pouhon" } };
        Assert.Equal("Pouhon", DebriefText.SpokenCorner(named));
        Assert.Equal("The hairpin", DebriefText.SpokenCorner(last with { Corner = last.Corner with { Label = "Hairpin" } }));
        Assert.Equal("The last curve", DebriefText.SpokenCorner(last with { Corner = last.Corner with { Label = "Last Curve" } }));
    }

    [Theory]
    [InlineData(0.04, "less than a tenth")]
    [InlineData(0.12, "a tenth")]
    [InlineData(0.35, "4 tenths")]
    [InlineData(1.26, "1.3 seconds")]
    public void TimeIsSpokenTheWayAnEngineerSaysIt(double seconds, string expected) =>
        Assert.Equal(expected, DebriefText.Tenths(seconds));

    [Fact]
    public void TheCoachDebriefsEveryCleanLapOfALiveSession()
    {
        var track = Support.TrackSimulator.Standard();
        var profile = track.SpeedProfile();
        var slowHairpin = track.SpeedProfile(new Dictionary<int, double> { [1] = 0.85 });
        var session = new CoachingSession(new CoachingOptions { CornerMapLaps = 3 });
        var debriefs = new List<LapDebrief>();
        PostLapCoach.Attach(session, debriefs.Add);

        foreach (var f in track.Drive([profile, profile, profile, slowHairpin, profile]))
        {
            session.Observe(f);
        }

        Assert.Equal(session.Analyses.Count, debriefs.Count);
        var slow = debriefs.MaxBy(d => d.LapTime)!;
        Assert.False(slow.IsBestLap);
        Assert.NotEmpty(slow.Corners);
        Assert.Contains(slow.Corners[0].Findings, f => f.Kind == FindingKind.LowMinimumSpeed);
    }

    private static LapAnalysis Analysis(int lapNumber, double lapTime, params CornerPass[] passes) =>
        new(new RecordedLap { LapNumber = lapNumber, SessionNum = 0, StartTime = 0, LapTime = lapTime, Flaws = LapFlaw.None }, passes, new Dictionary<string, double>());
}
