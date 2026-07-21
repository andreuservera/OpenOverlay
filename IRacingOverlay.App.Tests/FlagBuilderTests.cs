using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class FlagBuilderTests
{
    private static SyntheticMemoryBuilder FlagVars(out SyntheticMemoryBuilder builder)
    {
        builder = new SyntheticMemoryBuilder();
        builder.AddVar("SessionFlags", IrsdkVarType.BitField);
        return builder;
    }

    private static IRacingOverlay.Sdk.TelemetrySnapshot BuildWithFlags(uint flags)
    {
        FlagVars(out var builder);
        return TestSnapshotFactory.Build(builder, w => w.SetBitField("SessionFlags", flags));
    }

    [Fact]
    public void NoFlagsSet_ReturnsNone()
    {
        var state = FlagBuilder.Build(BuildWithFlags(0));

        Assert.Equal(FlagState.None.Name, state.Name);
    }

    [Fact]
    public void GreenBit_ReturnsGreen()
    {
        var state = FlagBuilder.Build(BuildWithFlags(0x00000004)); // irsdk_green

        Assert.Equal("GREEN", state.Name);
        Assert.False(state.IsCheckered);
        Assert.False(state.IsMeatball);
    }

    [Fact]
    public void ServicibleBit_ReturnsMeatball()
    {
        var state = FlagBuilder.Build(BuildWithFlags(0x00040000)); // irsdk_servicible

        Assert.Equal("SERVICE", state.Name);
        Assert.True(state.IsMeatball);
    }

    [Fact]
    public void CheckeredBit_ReturnsCheckered()
    {
        var state = FlagBuilder.Build(BuildWithFlags(0x00000001)); // irsdk_checkered

        Assert.Equal("CHECKERED", state.Name);
        Assert.True(state.IsCheckered);
    }

    [Fact]
    public void BlackAndYellowTogether_BlackTakesPriority()
    {
        const uint black = 0x00010000;
        const uint yellow = 0x00000008;

        var state = FlagBuilder.Build(BuildWithFlags(black | yellow));

        Assert.Equal("BLACK FLAG", state.Name);
    }

    [Fact]
    public void CautionWavingAndCaution_ReportsCaution()
    {
        const uint caution = 0x00004000;
        const uint cautionWaving = 0x00008000;

        var state = FlagBuilder.Build(BuildWithFlags(caution | cautionWaving));

        Assert.Equal("CAUTION", state.Name);
    }

    [Fact]
    public void MissingVariable_ReturnsNone()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var state = FlagBuilder.Build(snapshot);

        Assert.Equal(FlagState.None.Name, state.Name);
    }
}
