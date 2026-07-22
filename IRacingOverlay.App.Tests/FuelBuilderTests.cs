using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class FuelBuilderTests
{
    private static SyntheticMemoryBuilder FuelVars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("FuelLevel", IrsdkVarType.Float);
        builder.AddVar("FuelLevelPct", IrsdkVarType.Float);
        builder.AddVar("Lap", IrsdkVarType.Int);
        builder.AddVar("SessionLapsRemainEx", IrsdkVarType.Int);
        return builder;
    }

    private static FuelState Sample(SyntheticMemoryBuilder builder, FuelBuilder fuelBuilder, int lap, float fuelLevel, int? lapsRemain = null)
    {
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Lap", lap);
            w.SetFloat("FuelLevel", fuelLevel);
            w.SetFloat("FuelLevelPct", 0.5f);
            if (lapsRemain is { } laps)
            {
                w.SetInt("SessionLapsRemainEx", laps);
            }
        });
        return fuelBuilder.Build(snapshot);
    }

    [Fact]
    public void Build_NoLapCompletedYet_NoEstimateButLevelStillReported()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelBuilder();

        var state = Sample(builder, fuelBuilder, lap: 0, fuelLevel: 20f);

        Assert.False(state.HasEstimate);
        Assert.Equal(0, state.LapsOfFuelRemaining);
        Assert.Equal(20.0, state.LevelLiters, precision: 3);
    }

    [Fact]
    public void Build_OneLapCompleted_AveragesFromFuelDrop()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelBuilder();

        Sample(builder, fuelBuilder, lap: 0, fuelLevel: 20f);
        var state = Sample(builder, fuelBuilder, lap: 1, fuelLevel: 18f, lapsRemain: 15); // used 2L over lap 0

        Assert.True(state.HasEstimate);
        Assert.Equal(2.0, state.PerLapLiters, precision: 3);
        Assert.Equal(9.0, state.LapsOfFuelRemaining, precision: 3); // 18L / 2L-per-lap
        Assert.Equal(15, state.LapsRemainingInSession);
        Assert.False(state.WillMakeItToTheEnd); // 9 laps of fuel < 15 remaining
    }

    [Fact]
    public void Build_MultipleLaps_AveragesConsumptionAcrossAllOfThem()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelBuilder();

        Sample(builder, fuelBuilder, lap: 0, fuelLevel: 20f);
        Sample(builder, fuelBuilder, lap: 1, fuelLevel: 18f); // lap 0: -2L
        Sample(builder, fuelBuilder, lap: 2, fuelLevel: 15f); // lap 1: -3L
        var state = Sample(builder, fuelBuilder, lap: 3, fuelLevel: 13f); // lap 2: -2L

        // total 7L over 3 laps -> 2.333 L/lap average, not just the most recent lap's 2L.
        Assert.Equal(7.0 / 3.0, state.PerLapLiters, precision: 3);
    }

    [Fact]
    public void Build_PitStopRefuel_ExcludedFromAverage()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelBuilder();

        Sample(builder, fuelBuilder, lap: 0, fuelLevel: 20f);
        Sample(builder, fuelBuilder, lap: 1, fuelLevel: 18f); // lap 0: -2L, counted
        var state = Sample(builder, fuelBuilder, lap: 2, fuelLevel: 60f); // refueled during lap 1 -> excluded

        // Only lap 0's genuine 2L consumption counts; the refuel lap is skipped rather than
        // averaging in a negative/nonsensical "used -42L" figure.
        Assert.Equal(2.0, state.PerLapLiters, precision: 3);
    }

    [Fact]
    public void Build_LapCounterGoesBackwards_ResetsTheRunningAverage()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelBuilder();

        Sample(builder, fuelBuilder, lap: 0, fuelLevel: 20f);
        Sample(builder, fuelBuilder, lap: 1, fuelLevel: 18f); // establishes a 2L/lap average

        // Lap counter dropping back to 0 means a new session started (e.g. moved from qualify to
        // race) — stale data from the old session must not bleed into the new average.
        Sample(builder, fuelBuilder, lap: 0, fuelLevel: 60f);
        var state = Sample(builder, fuelBuilder, lap: 1, fuelLevel: 55f); // new session's lap 0: -5L

        Assert.Equal(5.0, state.PerLapLiters, precision: 3);
    }

    [Fact]
    public void Build_HugeLapsRemainSentinel_TreatedAsNoLimit()
    {
        var builder = FuelVars();
        var fuelBuilder = new FuelBuilder();

        Sample(builder, fuelBuilder, lap: 0, fuelLevel: 20f);
        var state = Sample(builder, fuelBuilder, lap: 1, fuelLevel: 18f, lapsRemain: 32767); // iRacing's "unlimited" sentinel

        Assert.Null(state.LapsRemainingInSession);
        Assert.Null(state.WillMakeItToTheEnd);
    }

    [Fact]
    public void Build_MissingVariable_ReturnsEmpty()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var state = new FuelBuilder().Build(snapshot);

        Assert.Equal(0, state.LevelLiters);
        Assert.False(state.HasEstimate);
    }
}
