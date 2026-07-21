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
    public void NoFlagsSet_ReturnsEmpty()
    {
        var flags = FlagBuilder.Build(BuildWithFlags(0));

        Assert.Empty(flags);
    }

    [Fact]
    public void GreenBit_ReturnsGreen()
    {
        var flags = FlagBuilder.Build(BuildWithFlags(0x00000004)); // irsdk_green

        var flag = Assert.Single(flags);
        Assert.Equal("GREEN", flag.Name);
        Assert.Equal(FlagVisualStyle.Solid, flag.Style);
    }

    [Fact]
    public void RepairBit_ReturnsMeatball()
    {
        var flags = FlagBuilder.Build(BuildWithFlags(0x00100000)); // irsdk_repair

        var flag = Assert.Single(flags);
        Assert.Equal("SERVICE", flag.Name);
        Assert.Equal(FlagVisualStyle.Meatball, flag.Style);
    }

    [Fact]
    public void ServicibleBit_IsNotAFlag_GreenStillWins()
    {
        // Regression test: irsdk_servicible ("car is allowed service") is not a flag at all per the
        // official SDK comment, but was previously mistaken for the meatball flag. It can legitimately
        // be set at the same time as green (e.g. around a rolling start) and must never override it.
        const uint green = 0x00000004;
        const uint servicible = 0x00040000;

        var flags = FlagBuilder.Build(BuildWithFlags(green | servicible));

        var flag = Assert.Single(flags);
        Assert.Equal("GREEN", flag.Name);
    }

    [Fact]
    public void CheckeredBit_ReturnsCheckered()
    {
        var flags = FlagBuilder.Build(BuildWithFlags(0x00000001)); // irsdk_checkered

        var flag = Assert.Single(flags);
        Assert.Equal("CHECKERED", flag.Name);
        Assert.Equal(FlagVisualStyle.Checkered, flag.Style);
    }

    [Fact]
    public void BlackAndYellowTogether_BlackTakesPriority()
    {
        const uint black = 0x00010000;
        const uint yellow = 0x00000008;

        var flags = FlagBuilder.Build(BuildWithFlags(black | yellow));

        var flag = Assert.Single(flags);
        Assert.Equal("BLACK FLAG", flag.Name);
    }

    [Fact]
    public void CautionWavingAndCaution_ReportsCaution()
    {
        const uint caution = 0x00004000;
        const uint cautionWaving = 0x00008000;

        var flags = FlagBuilder.Build(BuildWithFlags(caution | cautionWaving));

        var flag = Assert.Single(flags);
        Assert.Equal("CAUTION", flag.Name);
    }

    [Fact]
    public void DebrisAndYellowTogether_BothAppear()
    {
        // Debris is a supplementary flag, not part of the primary priority chain, so it should show
        // alongside whatever the primary track-state flag is instead of being hidden by it.
        const uint yellow = 0x00000008;
        const uint debris = 0x00000040;

        var flags = FlagBuilder.Build(BuildWithFlags(yellow | debris));

        Assert.Contains(flags, f => f.Name == "LOCAL YELLOW");
        Assert.Contains(flags, f => f.Name == "DEBRIS");
        Assert.Equal(2, flags.Count);
    }

    [Fact]
    public void BlueFlagDuringGreenRacing_BothAppear()
    {
        // Blue ("faster car behind") is a personal call that can happen during ordinary green-flag
        // racing, not just cautions — it must show alongside Green, not be suppressed by it.
        const uint green = 0x00000004;
        const uint blue = 0x00000020;

        var flags = FlagBuilder.Build(BuildWithFlags(green | blue));

        Assert.Contains(flags, f => f.Name == "GREEN");
        Assert.Contains(flags, f => f.Name == "BLUE — CAR BEHIND");
        Assert.Equal(2, flags.Count);
    }

    [Fact]
    public void DebrisYellowAndBlue_AllThreeAppear()
    {
        const uint yellow = 0x00000008;
        const uint debris = 0x00000040;
        const uint blue = 0x00000020;

        var flags = FlagBuilder.Build(BuildWithFlags(yellow | debris | blue));

        Assert.Equal(3, flags.Count);
        Assert.Contains(flags, f => f.Name == "LOCAL YELLOW");
        Assert.Contains(flags, f => f.Name == "DEBRIS");
        Assert.Contains(flags, f => f.Name == "BLUE — CAR BEHIND");
    }

    [Fact]
    public void DebrisFlag_UsesStripedStyle()
    {
        var flags = FlagBuilder.Build(BuildWithFlags(0x00000040)); // irsdk_debris

        var flag = Assert.Single(flags);
        Assert.Equal(FlagVisualStyle.DebrisStripes, flag.Style);
    }

    [Fact]
    public void BlueFlag_UsesOrangeStripeStyle()
    {
        var flags = FlagBuilder.Build(BuildWithFlags(0x00000020)); // irsdk_blue

        var flag = Assert.Single(flags);
        Assert.Equal(FlagVisualStyle.BlueWithOrangeStripe, flag.Style);
    }

    [Fact]
    public void MissingVariable_ReturnsEmpty()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var flags = FlagBuilder.Build(snapshot);

        Assert.Empty(flags);
    }
}
