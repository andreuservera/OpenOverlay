using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class LapLogTests
{
    private const int ParadeLaps = 3;
    private const int Racing = 4;
    private const int CoolDown = 6;

    private static SyntheticMemoryBuilder Vars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Lap", IrsdkVarType.Int);
        builder.AddVar("FuelLevel", IrsdkVarType.Float);
        builder.AddVar("OnPitRoad", IrsdkVarType.Bool);
        builder.AddVar("SessionState", IrsdkVarType.Int);
        builder.AddVar("SessionTime", IrsdkVarType.Double);
        builder.AddVar("LapDistPct", IrsdkVarType.Float);
        return builder;
    }

    private static void Sample(
        LapLog log, int lap, float fuel, double time, bool pitRoad = false, int state = Racing, float pct = 0.01f)
    {
        log.Observe(TestSnapshotFactory.Build(Vars(), w =>
        {
            w.SetInt("Lap", lap);
            w.SetFloat("FuelLevel", fuel);
            w.SetBool("OnPitRoad", pitRoad);
            w.SetInt("SessionState", state);
            w.SetDouble("SessionTime", time);
            w.SetFloat("LapDistPct", pct);
        }));
    }

    [Fact]
    public void RacingLaps_RecordFuelAndTime()
    {
        var log = new LapLog();

        Sample(log, lap: 1, fuel: 50, time: 100);
        Sample(log, lap: 2, fuel: 47, time: 190);
        Sample(log, lap: 3, fuel: 44.5f, time: 282);

        Assert.Equal(new[] { 3.0, 2.5 }, log.FuelPerLap.Select(v => Math.Round(v, 3)));
        Assert.Equal(new[] { 90.0, 92.0 }, log.LapSeconds);
        Assert.Equal(91.0, log.RecentLapSeconds(), precision: 3);
    }

    [Fact]
    public void FormationLap_IsLeftOut()
    {
        var log = new LapLog();

        Sample(log, lap: 0, fuel: 60, time: 10, state: ParadeLaps);
        Sample(log, lap: 0, fuel: 59, time: 60, state: ParadeLaps, pct: 0.6f);
        Sample(log, lap: 1, fuel: 58.5f, time: 120); // green: the formation lap ends here
        Sample(log, lap: 2, fuel: 55.5f, time: 210);

        Assert.Equal(new[] { 3.0 }, log.FuelPerLap.Select(v => Math.Round(v, 3)));
        Assert.Equal(new[] { 90.0 }, log.LapSeconds);
    }

    [Fact]
    public void LapThatTouchesPitRoad_IsLeftOut()
    {
        var log = new LapLog();

        Sample(log, lap: 1, fuel: 50, time: 0);
        Sample(log, lap: 2, fuel: 47, time: 90); // clean
        Sample(log, lap: 2, fuel: 45, time: 150, pitRoad: true, pct: 0.95f); // drive-through
        Sample(log, lap: 3, fuel: 44, time: 200, pitRoad: true); // crossed the line in the lane
        Sample(log, lap: 4, fuel: 41, time: 290); // the out lap from the lane
        Sample(log, lap: 5, fuel: 38, time: 380); // clean again

        Assert.Equal(new[] { 3.0, 3.0 }, log.FuelPerLap.Select(v => Math.Round(v, 3)));
        Assert.Equal(new[] { 90.0, 90.0 }, log.LapSeconds);
    }

    [Fact]
    public void CoolDownLap_IsLeftOut()
    {
        var log = new LapLog();

        Sample(log, lap: 10, fuel: 20, time: 0);
        Sample(log, lap: 11, fuel: 17, time: 90);
        Sample(log, lap: 11, fuel: 16, time: 100, state: CoolDown, pct: 0.2f);
        Sample(log, lap: 12, fuel: 15, time: 200, state: CoolDown);

        Assert.Single(log.FuelPerLap);
    }

    [Fact]
    public void AttachedMidLap_ThePartialLapIsLeftOut()
    {
        var log = new LapLog();

        Sample(log, lap: 4, fuel: 30, time: 0, pct: 0.42f);
        Sample(log, lap: 5, fuel: 28, time: 50);
        Sample(log, lap: 6, fuel: 25, time: 140);

        Assert.Equal(new[] { 3.0 }, log.FuelPerLap.Select(v => Math.Round(v, 3)));
    }

    [Fact]
    public void LapCounterJumpingSeveralLaps_IsNotMeasured()
    {
        // Nobody was watching those laps; whatever happened in them can't be read off the level.
        var log = new LapLog();

        Sample(log, lap: 1, fuel: 50, time: 0);
        Sample(log, lap: 4, fuel: 41, time: 270);

        Assert.Empty(log.FuelPerLap);
    }

    [Fact]
    public void FuelWidget_LeavesTheFormationLapOutOfItsAverage()
    {
        var log = new LapLog();
        var fuel = new FuelBuilder(log);
        var builder = Vars();
        builder.AddVar("FuelLevelPct", IrsdkVarType.Float);

        FuelState Tick(int lap, float level, double time, int state)
        {
            var snapshot = TestSnapshotFactory.Build(builder, w =>
            {
                w.SetInt("Lap", lap);
                w.SetFloat("FuelLevel", level);
                w.SetInt("SessionState", state);
                w.SetDouble("SessionTime", time);
            });
            log.Observe(snapshot);
            return fuel.Build(snapshot);
        }

        Tick(0, 60, 0, ParadeLaps);
        Tick(1, 59, 100, Racing); // formation lap: 1 L at pace-car speed
        var state = Tick(2, 56, 190, Racing);

        Assert.Equal(3.0, state.PerLapLiters, precision: 3);
    }
}
