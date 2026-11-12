using IRacingOverlay.Coaching.Advice;
using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public class CornerTypeTests
{
    private static readonly Corner C = new("T1", 100, 250, 350);

    // A braking corner taken at 80 km/h from 200 km/h.
    private static CornerPass Braking(double time = 5) => new()
    {
        Corner = C,
        LapNumber = 1,
        SegmentTime = time,
        EntrySpeed = 200 / 3.6f,
        BrakePointM = 120,
        PeakBrake = 0.9f,
        BrakeRampM = 5,
        BrakeReleaseM = 200,
        BrakeLengthM = 80,
        TurnInM = 180,
        TrailBrakeM = 20,
        MinSpeed = 80 / 3.6f,
        MinSpeedAtM = 250,
        ThrottlePickupM = 260,
        FullThrottleM = 300,
        ExitSpeed = 30,
        ExitSpeedPlus100 = 40,
        GearAtApex = 2,
        LiftPointM = 118,
        LiftLengthM = 182,
        MinThrottle = 0,
        SteerPerLatAccel = 0.02,
    };

    // A fast lift corner: 250 km/h in, throttle to 50% for 60 m, 230 km/h at the apex.
    private static CornerPass Lift(double time = 3) => new()
    {
        Corner = C,
        LapNumber = 1,
        SegmentTime = time,
        EntrySpeed = 250 / 3.6f,
        MinSpeed = 230 / 3.6f,
        MinSpeedAtM = 250,
        ThrottlePickupM = 200,
        FullThrottleM = 220,
        ExitSpeed = 240 / 3.6f,
        ExitSpeedPlus100 = 245 / 3.6f,
        GearAtApex = 6,
        LiftPointM = 160,
        LiftLengthM = 60,
        MinThrottle = 0.5f,
    };

    private static CornerPass Flat() => Lift() with { LiftPointM = null, LiftLengthM = 0, MinThrottle = 1 };

    [Fact]
    public void CornersAreSortedByHowTheBestPassTookThem()
    {
        Assert.Equal(CornerType.Slow, CornerTypes.Classify(Braking()));
        Assert.Equal(CornerType.BrakingFast, CornerTypes.Classify(Braking() with { MinSpeed = 150 / 3.6f }));
        Assert.Equal(CornerType.Lift, CornerTypes.Classify(Lift()));
        Assert.Equal(CornerType.Flat, CornerTypes.Classify(Flat()));
        Assert.Equal(CornerType.Complex, CornerTypes.Classify(Braking() with { SpeedDips = 2 }));

        // A short dab of brake is still a lift corner.
        Assert.Equal(CornerType.Lift, CornerTypes.Classify(Lift() with { BrakePointM = 170, PeakBrake = 0.1f, BrakeLengthM = 10 }));
    }

    [Fact]
    public void TheSimulatedTrackHasSlowAndFastBrakingCorners()
    {
        var track = TrackSimulator.Standard();
        var normal = track.SpeedProfile();
        var session = new CoachingSession();
        foreach (var f in track.Drive([normal, normal]))
        {
            session.Observe(f);
        }

        var types = session.CornerMap!.Corners.Select(c => CornerTypes.Classify(session.BestPasses[c.Name])).ToList();
        Assert.Equal([CornerType.Slow, CornerType.BrakingFast, CornerType.Slow], types);
    }

    [Fact]
    public void ALiftIsMeasuredWhereTheThrottleComesOff()
    {
        var lap = new LapTrace(600);
        for (var i = 0; i < lap.Length; i++)
        {
            lap.Speed[i] = i is >= 220 and <= 300 ? 62 : 68;
            lap.Time[i] = i / 65f;
            lap.Throttle[i] = i is >= 200 and < 260 ? 0.5f : 1f;
            lap.Steering[i] = i is >= 220 and <= 320 ? 0.05f : 0;
            lap.LatAccel[i] = i is >= 220 and <= 320 ? 25 : 0;
        }

        var p = CornerAnalyzer.Measure(lap, new Corner("T1", 200, 260, 340), 1);

        Assert.Equal(200, p.LiftPointM);
        Assert.Equal(0.5f, p.MinThrottle);
        Assert.Equal(260, p.FullThrottleM);
        Assert.Equal(60, p.LiftLengthM);
        Assert.Null(p.BrakePointM);
        Assert.Equal(CornerType.Lift, CornerTypes.Classify(p));
    }

    [Fact]
    public void InALiftCornerTheCoachTalksAboutTheLift()
    {
        var best = Lift();
        var pass = Lift(3.2) with { LiftPointM = 140, LiftLengthM = 90, MinThrottle = 0.2f, MinSpeed = 225 / 3.6f };

        var kinds = CornerDiagnoser.Diagnose(pass, best).Select(f => f.Kind).ToList();

        Assert.Equal([FindingKind.LiftedTooEarly, FindingKind.LiftTooDeep, FindingKind.LiftTooLong, FindingKind.LowMinimumSpeed], kinds);
        Assert.Equal("lift 20 metres later", DebriefText.Advice(CornerDiagnoser.Diagnose(pass, best)[0]));
    }

    [Fact]
    public void BrakingWhereALiftWasEnoughComesFirst()
    {
        var pass = Lift(3.3) with { BrakePointM = 150, PeakBrake = 0.5f, BrakeLengthM = 40, MinThrottle = 0 };

        var first = CornerDiagnoser.Diagnose(pass, Lift())[0];

        Assert.Equal(FindingKind.UnneededBrake, first.Kind);
        Assert.Equal("no need to brake, a lift is enough", DebriefText.Advice(first));
    }

    [Fact]
    public void LiftingInAFlatCornerIsCalledOut()
    {
        var pass = Lift(3.1);

        var f = CornerDiagnoser.Diagnose(pass, Flat())[0];

        Assert.Equal(FindingKind.LiftedInFlatCorner, f.Kind);
        Assert.Equal("it's flat, keep your foot in", DebriefText.Advice(f));
    }

    [Fact]
    public void SlowCornersAreCheckedForCoastingUndersteerAndTraction()
    {
        var best = Braking();
        var pass = Braking(5.3) with
        {
            CoastingM = 25,
            SteerPerLatAccel = 0.026,
            MinSpeed = 76 / 3.6f,
            ThrottleLifts = 1,
        };

        var kinds = CornerDiagnoser.Diagnose(pass, best).Select(f => f.Kind).ToList();

        Assert.Equal([FindingKind.Coasting, FindingKind.Understeer, FindingKind.LowMinimumSpeed, FindingKind.ThrottleHesitation], kinds);
    }

    [Fact]
    public void InAChicaneTheExitComesFirst()
    {
        var best = Braking() with { SpeedDips = 2 };
        var pass = best with { SegmentTime = 5.3, BrakePointM = 105, ExitSpeedPlus100 = 37 };

        var kinds = CornerDiagnoser.Diagnose(pass, best).Select(f => f.Kind).ToList();

        Assert.Equal([FindingKind.SlowExit, FindingKind.BrakedTooEarly], kinds);
    }

    [Fact]
    public void TheWrittenDebriefSaysTheCornerType()
    {
        var best = Lift();
        var advice = new CornerAdvice(C with { Label = "Blanchimont" }, 0.2, [new CornerFinding(FindingKind.LiftedTooEarly, 20)], 0) { Best = best };
        var d = new LapDebrief(3, 140, 0.4, false, [advice]);

        Assert.Contains(DebriefText.Lines(d), l => l.StartsWith("  Blanchimont (lift): lost 0.20 s. Lift 20 metres later"));
    }

    [Fact]
    public void ACornerKeepsTheTypeMostOfItsPassesShow()
    {
        var chicane = Braking() with { SpeedDips = 2 };
        var passes = new[] { chicane, chicane with { LapNumber = 2 }, Braking() with { LapNumber = 3, SegmentTime = 4.9 } };

        // The best pass (lap 3) was taken as one slow corner, but the corner is a chicane.
        Assert.Equal(CornerType.Complex, CornerTypes.Consensus(passes, passes[2]));
        // A tie goes to the best pass.
        Assert.Equal(CornerType.Slow, CornerTypes.Consensus(passes.Skip(1), passes[2]));
    }

    [Fact]
    public void CoastingWithoutBrakingIsFixedWithThrottle()
    {
        var best = Braking() with { BrakePointM = null, PeakBrake = 0, BrakeLengthM = 0, SpeedDips = 2 };
        var pass = best with { SegmentTime = 5.3, CoastingM = 35 };

        var f = Assert.Single(CornerDiagnoser.Diagnose(pass, best), x => x.Kind == FindingKind.Coasting);

        Assert.Equal("35 metres coasting, keep some throttle on through it", DebriefText.Advice(f));
    }

    [Fact]
    public void ALiftThatBeganInTheCornerBeforeStillPicksUpInThisOne()
    {
        var lap = new LapTrace(600);
        for (var i = 0; i < lap.Length; i++)
        {
            lap.Speed[i] = i is >= 220 and <= 300 ? 50 : 55;
            lap.Time[i] = i / 52f;
            // Partial throttle from the corner before (at 150 m) until 240 m, never back to full in between.
            lap.Throttle[i] = i is >= 150 and < 240 ? 0.3f : 1f;
            lap.Steering[i] = i is >= 210 and <= 300 ? 0.05f : 0;
        }

        var p = CornerAnalyzer.Measure(lap, new Corner("T2", 200, 260, 320), 1);

        Assert.Equal(150, p.LiftPointM);
        Assert.True(p.ThrottlePickupM >= 200);
        Assert.Equal(240, p.FullThrottleM);
    }

    [Fact]
    public void TheWrittenDebriefListsAtMostThreeFixesPerCorner()
    {
        var findings = new[] { FindingKind.BrakedTooEarly, FindingKind.InitialBrakeTooSoft, FindingKind.TooLittleTrailBrake, FindingKind.LowMinimumSpeed, FindingKind.LateThrottle }
            .Select(k => new CornerFinding(k, 10)).ToList();
        var d = new LapDebrief(3, 140, 0.4, false, [new CornerAdvice(C with { Label = "Pouhon" }, 0.3, findings, 0) { Best = Braking() }]);

        var line = Assert.Single(DebriefText.Lines(d), l => l.Contains("Pouhon"));

        Assert.Equal(2, line.Count(ch => ch == ';'));
    }

    [Fact]
    public void TheTypeIsSettledByTheFirstFivePasses()
    {
        var lift = Lift();
        var dab = Lift() with { BrakePointM = 170, PeakBrake = 0.25f, BrakeLengthM = 30, MinSpeed = 200 / 3.6f };
        var passes = new List<CornerPass> { lift, lift with { LapNumber = 2 }, lift with { LapNumber = 3 }, dab with { LapNumber = 4 }, dab with { LapNumber = 5 } };
        Assert.Equal(CornerType.Lift, CornerTypes.Consensus(passes));

        passes.AddRange(Enumerable.Range(6, 4).Select(l => dab with { LapNumber = l }));
        Assert.Equal(CornerType.Lift, CornerTypes.Consensus(passes, passes[^1]));
    }

    [Fact]
    public void ALiftCarriedOnFromTheCornerBeforeIsNotALiftPointOfThisOne()
    {
        var lap = new LapTrace(600);
        for (var i = 0; i < lap.Length; i++)
        {
            lap.Speed[i] = 55;
            lap.Time[i] = i / 55f;
            // Partial throttle from the corner before (60 m) right through this one's piece (from 150 m).
            lap.Throttle[i] = i is >= 60 and < 260 ? 0.6f : 1f;
            lap.Steering[i] = i is >= 210 and <= 300 ? 0.05f : 0;
        }

        var p = CornerAnalyzer.Measure(lap, new Corner("Fagnes 2", 200, 250, 320), 1, pieceStartM: 150);
        Assert.Equal(150, p.LiftPointM);
        Assert.True(p.LiftCarriedOver);

        // The best pass went back to full throttle and lifted at 230: no "lift 80 metres later".
        var best = p with { LiftPointM = 230, LiftCarriedOver = false, LiftLengthM = 30, SegmentTime = 4 };
        var pass = p with { SegmentTime = 4.2 };
        Assert.DoesNotContain(CornerDiagnoser.Diagnose(pass, best), f => f.Kind == FindingKind.LiftedTooEarly);
    }
}
