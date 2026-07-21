using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class DeltaBuilderTests
{
    [Fact]
    public void Build_SessionBest_ReadsCorrectVariable()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LapDeltaToSessionBestLap", IrsdkVarType.Float);
        builder.AddVar("LapDeltaToBestLap", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("LapDeltaToSessionBestLap", -0.512f);
            w.SetFloat("LapDeltaToBestLap", 1.234f);
        });

        var state = DeltaBuilder.Build(snapshot, DeltaReference.SessionBest);

        Assert.True(state.IsValid);
        Assert.Equal(-0.512, state.DeltaSeconds, precision: 3);
        Assert.Equal("VS SESSION BEST", state.ReferenceLabel);
    }

    [Fact]
    public void Build_PersonalBestAllTime_ReadsCorrectVariable()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LapDeltaToSessionBestLap", IrsdkVarType.Float);
        builder.AddVar("LapDeltaToBestLap", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("LapDeltaToSessionBestLap", -0.512f);
            w.SetFloat("LapDeltaToBestLap", 1.234f);
        });

        var state = DeltaBuilder.Build(snapshot, DeltaReference.PersonalBestAllTime);

        Assert.Equal(1.234, state.DeltaSeconds, precision: 3);
        Assert.Equal("VS ALL-TIME BEST", state.ReferenceLabel);
    }

    [Fact]
    public void Build_OptimalLap_ReadsCorrectVariable()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LapDeltaToOptimalLap", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("LapDeltaToOptimalLap", -1.5f));

        var state = DeltaBuilder.Build(snapshot, DeltaReference.OptimalLap);

        Assert.Equal(-1.5, state.DeltaSeconds, precision: 3);
        Assert.Equal("VS OPTIMAL LAP", state.ReferenceLabel);
    }

    [Fact]
    public void Build_MissingVariable_ReportsInvalid()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var state = DeltaBuilder.Build(snapshot, DeltaReference.SessionBest);

        Assert.False(state.IsValid);
        Assert.Equal("—", state.Display);
    }

    [Fact]
    public void Build_OkCompanionFalse_ReportsInvalidEvenWithAZeroDelta()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LapDeltaToSessionBestLap", IrsdkVarType.Float);
        builder.AddVar("LapDeltaToSessionBestLap_OK", IrsdkVarType.Bool);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("LapDeltaToSessionBestLap", 0f);
            w.SetBool("LapDeltaToSessionBestLap_OK", false);
        });

        var state = DeltaBuilder.Build(snapshot, DeltaReference.SessionBest);

        Assert.False(state.IsValid);
    }

    [Fact]
    public void Display_NegativeDelta_ShowsMinusSign()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LapDeltaToSessionBestLap", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("LapDeltaToSessionBestLap", -0.256f));

        var state = DeltaBuilder.Build(snapshot, DeltaReference.SessionBest);

        Assert.Equal("-0.256", state.Display);
    }

    [Fact]
    public void Display_PositiveDelta_ShowsPlusSign()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LapDeltaToSessionBestLap", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("LapDeltaToSessionBestLap", 0.812f));

        var state = DeltaBuilder.Build(snapshot, DeltaReference.SessionBest);

        Assert.Equal("+0.812", state.Display);
    }
}
