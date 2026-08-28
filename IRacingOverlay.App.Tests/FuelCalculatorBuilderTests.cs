using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class FuelCalculatorBuilderTests
{
    private static IracingSessionInfo SessionWithTank(double capacityLiters, double maxFuelPct = 1) => new()
    {
        DriverInfo = new DriverInfoSection
        {
            DriverCarFuelMaxLtr = capacityLiters,
            DriverCarMaxFuelPct = maxFuelPct,
        },
    };
    private static SyntheticMemoryBuilder FuelVars(bool timedSession = false)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("FuelLevel", IrsdkVarType.Float);
        builder.AddVar("FuelLevelPct", IrsdkVarType.Float);
        builder.AddVar("Lap", IrsdkVarType.Int);
        builder.AddVar("SessionLapsRemainEx", IrsdkVarType.Int);
        if (timedSession)
        {
            builder.AddVar("SessionTimeRemain", IrsdkVarType.Double);
            builder.AddVar("LapLastLapTime", IrsdkVarType.Float);
        }

        return builder;
    }

    private static FuelCalculatorState Sample(
        SyntheticMemoryBuilder builder,
        FuelCalculatorBuilder fuelBuilder,
        FuelCalculatorOptions options,
        int lap,
        float fuelLevel,
        int lapsRemain = int.MaxValue,
        double? timeRemain = null,
        float lastLapTime = 0,
        IracingSessionInfo? session = null)
    {
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Lap", lap);
            w.SetFloat("FuelLevel", fuelLevel);
            w.SetFloat("FuelLevelPct", 0.5f);
            w.SetInt("SessionLapsRemainEx", lapsRemain);
            if (timeRemain is { } seconds)
            {
                w.SetDouble("SessionTimeRemain", seconds);
                w.SetFloat("LapLastLapTime", lastLapTime);
            }
        });
        return fuelBuilder.Build(snapshot, session, options);
    }

    [Fact]
    public void Build_TracksLastMinMaxAndAverageAcrossLaps()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions { AverageSource = FuelAverageSource.AllSession };

        Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 50f);
        Sample(builder, fuelBuilder, options, lap: 1, fuelLevel: 48f); // 2.0
        Sample(builder, fuelBuilder, options, lap: 2, fuelLevel: 45f); // 3.0
        var state = Sample(builder, fuelBuilder, options, lap: 3, fuelLevel: 44f); // 1.0

        Assert.Equal(1.0, state.LastLapLiters, precision: 3);
        Assert.Equal(1.0, state.MinLiters, precision: 3);
        Assert.Equal(3.0, state.MaxLiters, precision: 3);
        Assert.Equal(2.0, state.AverageLiters, precision: 3); // (2 + 3 + 1) / 3
    }

    [Fact]
    public void Build_RollingAverageWindow_OnlyUsesTrailingLaps()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions { AverageSource = FuelAverageSource.Last3Laps };

        Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 100f);
        Sample(builder, fuelBuilder, options, lap: 1, fuelLevel: 90f); // 10.0 — outside the window
        Sample(builder, fuelBuilder, options, lap: 2, fuelLevel: 88f); // 2.0
        Sample(builder, fuelBuilder, options, lap: 3, fuelLevel: 85f); // 3.0
        var state = Sample(builder, fuelBuilder, options, lap: 4, fuelLevel: 81f); // 4.0

        Assert.Equal(3.0, state.AverageLiters, precision: 3); // (2 + 3 + 4) / 3, the 10.0 lap dropped
        Assert.Equal(10.0, state.MaxLiters, precision: 3); // min/max stay whole-session
    }

    [Fact]
    public void Build_TimedSession_DerivesLapsLeftFromClock()
    {
        // The whole point of this builder over FuelBuilder: iRacing reports no lap limit for a timed
        // race, so without the clock fallback every to-the-finish figure would be unanswerable.
        var builder = FuelVars(timedSession: true);
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions { MarginLaps = 0, MarginLiters = 0 };

        Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 50f, timeRemain: 600, lastLapTime: 90f);
        var state = Sample(builder, fuelBuilder, options, lap: 1, fuelLevel: 48f, timeRemain: 600, lastLapTime: 90f);

        // 600s / 90s per lap = 6.67 -> rounded up, the lap in progress at the flag still needs fuel.
        Assert.Equal(7, state.LapsLeftInSession);
        Assert.True(state.CanProjectToFinish);
        Assert.Equal(14.0, state.FuelToFinishLiters, precision: 3); // 7 laps * 2.0 L
        Assert.Equal(34.0, state.FuelDeltaLiters, precision: 3); // 48 - 14, plenty spare
    }

    [Fact]
    public void Build_LapLimitedSession_PrefersLapCounterOverClock()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions { MarginLaps = 0, MarginLiters = 0 };

        Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 20f, lapsRemain: 10);
        var state = Sample(builder, fuelBuilder, options, lap: 1, fuelLevel: 18f, lapsRemain: 10);

        Assert.Equal(10, state.LapsLeftInSession);
        Assert.Equal(20.0, state.FuelToFinishLiters, precision: 3); // 10 laps * 2.0 L
        Assert.Equal(-2.0, state.FuelDeltaLiters, precision: 3); // 18 in the tank, 20 needed
        Assert.True(state.NeedsRefuel);
        Assert.Equal(2, state.RefuelLiters, precision: 3);
    }

    [Fact]
    public void Build_SafetyMargin_IncreasesFuelNeeded()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions { MarginLaps = 1, MarginLiters = 0.5 };

        Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 20f, lapsRemain: 10);
        var state = Sample(builder, fuelBuilder, options, lap: 1, fuelLevel: 18f, lapsRemain: 10);

        // (10 laps + 1 margin lap) * 2.0 L + 0.5 L flat = 22.5 L
        Assert.Equal(22.5, state.FuelToFinishLiters, precision: 3);
        Assert.Equal(5, state.RefuelLiters, precision: 3); // 4.5 short, rounded up to a whole liter
    }

    [Fact]
    public void Build_RefuelLap_ExcludedFromConsumption()
    {
        // Fuel going UP is a pit stop, not a lap of running — folding that in would record a
        // negative consumption and poison every downstream figure.
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions();

        Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 20f);
        Sample(builder, fuelBuilder, options, lap: 1, fuelLevel: 18f); // 2.0 burned
        var state = Sample(builder, fuelBuilder, options, lap: 2, fuelLevel: 60f); // refuelled

        Assert.Equal(2.0, state.AverageLiters, precision: 3);
        Assert.Equal(2.0, state.LastLapLiters, precision: 3);
    }

    [Fact]
    public void Build_OpenPracticeWithNoSessionEnd_HidesToFinishFigures()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions();

        Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 20f);
        var state = Sample(builder, fuelBuilder, options, lap: 1, fuelLevel: 18f);

        Assert.Null(state.LapsLeftInSession);
        Assert.False(state.CanProjectToFinish);
        Assert.False(state.IsShortOfFuel);
        Assert.Equal("—", state.FuelDeltaDisplay);
        Assert.Equal("—", state.FuelToFinishDisplay);
        // Laps-of-fuel doesn't depend on a session end, so it still reports.
        Assert.Equal(9.0, state.LapsRemainingWithFuel, precision: 3);
    }

    [Fact]
    public void Build_NewSession_ResetsHistory()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions();

        Sample(builder, fuelBuilder, options, lap: 5, fuelLevel: 30f);
        Sample(builder, fuelBuilder, options, lap: 6, fuelLevel: 20f); // 10 L/lap, previous session

        var state = Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 50f); // lap counter reset

        Assert.Equal(0, state.AverageLiters);
        Assert.Equal(0, state.MaxLiters);
    }

    [Fact]
    public void Build_UnlimitedTimeSentinel_IsNotTreatedAsASessionClock()
    {
        // Reported live from test drive: the out-lap read "—", then the moment a lap time existed
        // the widget showed TO FINISH -16460 L. iRacing's "no time limit" sentinel is a 7-day clock,
        // which a too-generous threshold accepted as a real session length worth ~5,500 laps.
        const double SevenDaysInSeconds = 604_800;

        var builder = FuelVars(timedSession: true);
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions();
        var session = SessionWithTank(capacityLiters: 60);

        Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 50f,
            timeRemain: SevenDaysInSeconds, lastLapTime: 110f, session: session);
        var state = Sample(builder, fuelBuilder, options, lap: 1, fuelLevel: 47f,
            timeRemain: SevenDaysInSeconds, lastLapTime: 110f, session: session);

        Assert.Null(state.LapsLeftInSession);
        Assert.False(state.CanProjectToFinish);
        Assert.False(state.IsShortOfFuel);
        Assert.Equal("—", state.FuelToFinishDisplay);
        // Falls through to the fill-to-max recommendation instead.
        Assert.Equal("13 L TO FULL", state.RefuelDisplay);
    }

    [Fact]
    public void Build_TwentyFourHourRace_StillCountsAsARealSessionClock()
    {
        // The threshold must not be so tight that it rejects iRacing's longest genuine race.
        const double TwentyFourHours = 24 * 60 * 60;

        var builder = FuelVars(timedSession: true);
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions { MarginLaps = 0, MarginLiters = 0 };

        Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 50f, timeRemain: TwentyFourHours, lastLapTime: 120f);
        var state = Sample(builder, fuelBuilder, options, lap: 1, fuelLevel: 48f, timeRemain: TwentyFourHours, lastLapTime: 120f);

        Assert.Equal(720, state.LapsLeftInSession); // 86400s / 120s
        Assert.True(state.CanProjectToFinish);
    }

    [Fact]
    public void Build_NoFuelVariable_ReturnsEmpty()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var state = new FuelCalculatorBuilder().Build(snapshot, session: null, new FuelCalculatorOptions());

        Assert.Equal(0, state.LevelLiters);
        Assert.False(state.HasUsageEstimate);
    }

    private static SyntheticMemoryBuilder OnTrackFuelVars()
    {
        var builder = FuelVars();
        builder.AddVar("IsOnTrack", IrsdkVarType.Bool);
        return builder;
    }

    private static FuelCalculatorState SampleOnTrack(
        SyntheticMemoryBuilder builder,
        FuelCalculatorBuilder fuelBuilder,
        FuelCalculatorOptions options,
        int lap,
        float fuelLevel,
        bool isOnTrack)
    {
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Lap", lap);
            w.SetFloat("FuelLevel", fuelLevel);
            w.SetFloat("FuelLevelPct", 0.5f);
            w.SetInt("SessionLapsRemainEx", int.MaxValue);
            w.SetBool("IsOnTrack", isOnTrack);
        });
        return fuelBuilder.Build(snapshot, session: null, options);
    }

    [Fact]
    public void Build_OutLapAfterGarageFuelling_IsCounted()
    {
        // Reported live: nothing showed up until the first flying lap was complete. The baseline was
        // being taken on the garage screen before race fuel was loaded, so finishing the out-lap
        // looked like the tank had gained fuel and the lap was thrown away as a refuel.
        var builder = OnTrackFuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions();

        SampleOnTrack(builder, fuelBuilder, options, lap: 0, fuelLevel: 5f, isOnTrack: false); // garage
        SampleOnTrack(builder, fuelBuilder, options, lap: 0, fuelLevel: 50f, isOnTrack: true); // in the car, fuelled
        var state = SampleOnTrack(builder, fuelBuilder, options, lap: 1, fuelLevel: 47f, isOnTrack: true); // out-lap done

        Assert.Equal(3.0, state.LastLapLiters, precision: 3);
        Assert.Equal(3.0, state.AverageLiters, precision: 3);
        Assert.Equal(3.0, state.MinLiters, precision: 3);
        Assert.True(state.HasUsageEstimate);
    }

    [Fact]
    public void Build_GarageFuelReducedBeforeDriving_DoesNotInflateOutLap()
    {
        // The mirror image: dropping the fuel load in the garage before heading out would otherwise
        // be charged to the out-lap as an enormous consumption figure.
        var builder = OnTrackFuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions();

        SampleOnTrack(builder, fuelBuilder, options, lap: 0, fuelLevel: 80f, isOnTrack: false);
        SampleOnTrack(builder, fuelBuilder, options, lap: 0, fuelLevel: 20f, isOnTrack: false); // trimmed in the garage
        SampleOnTrack(builder, fuelBuilder, options, lap: 0, fuelLevel: 20f, isOnTrack: true);
        var state = SampleOnTrack(builder, fuelBuilder, options, lap: 1, fuelLevel: 18f, isOnTrack: true);

        Assert.Equal(2.0, state.LastLapLiters, precision: 3);
    }

    [Fact]
    public void Build_MidLapRefuel_DiscardsThatLapEntirely()
    {
        // A lap containing a pit stop can't be measured from the level difference at all: what's
        // left after the stop only covers the run from pit exit to the line, not the whole lap.
        var builder = OnTrackFuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions();

        SampleOnTrack(builder, fuelBuilder, options, lap: 0, fuelLevel: 20f, isOnTrack: true);
        SampleOnTrack(builder, fuelBuilder, options, lap: 1, fuelLevel: 18f, isOnTrack: true); // clean lap: 2.0
        SampleOnTrack(builder, fuelBuilder, options, lap: 1, fuelLevel: 60f, isOnTrack: true); // refuelled mid-lap
        SampleOnTrack(builder, fuelBuilder, options, lap: 2, fuelLevel: 59f, isOnTrack: true); // pit lap discarded
        var state = SampleOnTrack(builder, fuelBuilder, options, lap: 3, fuelLevel: 56f, isOnTrack: true); // 3.0

        Assert.Equal(new[] { 2.0, 3.0 }, new[] { state.MinLiters, state.MaxLiters });
        Assert.Equal(2.5, state.AverageLiters, precision: 3); // only the two clean laps
    }

    [Fact]
    public void Build_NoSessionEnd_RecommendsFillingTheTank()
    {
        // Test drive / open practice: there's no finish to compute a need against, so the only
        // sensible recommendation is to top the tank up to its maximum.
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions();
        var session = SessionWithTank(capacityLiters: 60);

        var state = Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 22.5f, session: session);

        Assert.False(state.CanProjectToFinish);
        Assert.Equal(60, state.TankCapacityLiters, precision: 3);
        Assert.Equal(38, state.RefuelLiters, precision: 3); // 60 - 22.5, rounded up
        Assert.Equal("38 L TO FULL", state.RefuelDisplay);
        Assert.False(state.IsShortOfFuel); // routine top-up, not a warning
    }

    [Fact]
    public void Build_NoSessionEnd_TankAlreadyFull_ReportsFull()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();

        var state = Sample(builder, fuelBuilder, new FuelCalculatorOptions(), lap: 0, fuelLevel: 60f,
            session: SessionWithTank(capacityLiters: 60));

        Assert.Equal(0, state.RefuelLiters);
        Assert.Equal("FULL", state.RefuelDisplay);
    }

    [Fact]
    public void Build_SeriesFuelRestriction_ReducesUsableCapacity()
    {
        // Some series cap how full the tank may be run; the physical size alone would over-recommend.
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();

        var state = Sample(builder, fuelBuilder, new FuelCalculatorOptions(), lap: 0, fuelLevel: 10f,
            session: SessionWithTank(capacityLiters: 100, maxFuelPct: 0.5));

        Assert.Equal(50, state.TankCapacityLiters, precision: 3);
        Assert.Equal(40, state.RefuelLiters, precision: 3);
    }

    [Fact]
    public void Build_NoSessionInfoYet_DerivesCapacityFromFuelPercentage()
    {
        // Session YAML may not be parsed yet on the first ticks — FuelLevelPct is a fraction of
        // maximum, so capacity is recoverable from telemetry alone in the meantime.
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();

        // Sample() writes FuelLevelPct as 0.5, so 30 L at 50% implies a 60 L tank.
        var state = Sample(builder, fuelBuilder, new FuelCalculatorOptions(), lap: 0, fuelLevel: 30f);

        Assert.Equal(60, state.TankCapacityLiters, precision: 3);
        Assert.Equal(30, state.RefuelLiters, precision: 3);
    }

    [Fact]
    public void Build_KnownFinishWithEnoughFuel_StillReportsNotNeeded()
    {
        // A known finish must keep taking priority over the fill-to-max fallback.
        var builder = FuelVars();
        var fuelBuilder = new FuelCalculatorBuilder();
        var options = new FuelCalculatorOptions { MarginLaps = 0, MarginLiters = 0 };
        var session = SessionWithTank(capacityLiters: 60);

        Sample(builder, fuelBuilder, options, lap: 0, fuelLevel: 50f, lapsRemain: 5, session: session);
        var state = Sample(builder, fuelBuilder, options, lap: 1, fuelLevel: 48f, lapsRemain: 5, session: session);

        Assert.True(state.CanProjectToFinish);
        Assert.Equal(0, state.RefuelLiters);
        Assert.Equal("NOT NEEDED", state.RefuelDisplay);
    }
}
