using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class TireInfoBuilderTests
{
    [Fact]
    public void Build_ReadsPressureAndAveragesTempAcrossTread()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LFpressure", IrsdkVarType.Float);
        builder.AddVar("LFcoldPressure", IrsdkVarType.Float);
        builder.AddVar("LFtempCL", IrsdkVarType.Float);
        builder.AddVar("LFtempCM", IrsdkVarType.Float);
        builder.AddVar("LFtempCR", IrsdkVarType.Float);

        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("LFpressure", 180.0f);
            w.SetFloat("LFcoldPressure", 165.0f);
            w.SetFloat("LFtempCL", 80.0f);
            w.SetFloat("LFtempCM", 90.0f);
            w.SetFloat("LFtempCR", 100.0f);
        });

        var state = TireInfoBuilder.Build(snapshot);

        Assert.Equal(180.0, state.LF.PressureKPa);
        Assert.Equal(165.0, state.LF.ColdPressureKPa);
        Assert.Equal(90.0, state.LF.TempC, precision: 3);
    }

    [Fact]
    public void Build_MissingVariables_DoesNotThrowAndReportsUnavailable()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var state = TireInfoBuilder.Build(snapshot);

        Assert.Equal("—", state.LF.PressureDisplay);
        Assert.Equal("—", state.RF.TempDisplay);
        Assert.Equal("LR", state.LR.Label);
        Assert.Equal("RR", state.RR.Label);
    }
}
