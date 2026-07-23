using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class TireInfoBuilderTests
{
    [Fact]
    public void Build_FallsBackToCarcassTemp_WhenSurfaceTempUnavailable()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LFcoldPressure", IrsdkVarType.Float);
        builder.AddVar("LFtempCL", IrsdkVarType.Float);
        builder.AddVar("LFtempCM", IrsdkVarType.Float);
        builder.AddVar("LFtempCR", IrsdkVarType.Float);

        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("LFcoldPressure", 165.0f);
            w.SetFloat("LFtempCL", 80.0f);
            w.SetFloat("LFtempCM", 90.0f);
            w.SetFloat("LFtempCR", 100.0f);
        });

        var state = TireInfoBuilder.Build(snapshot);

        // iRacing never exposes a live/"hot" pressure channel on any car — cold/garage-set pressure
        // is the only pressure telemetry that actually exists, so it's what the panel shows.
        Assert.Equal(165.0, state.LF.PressureKPa);
        Assert.Equal(165.0, state.LF.ColdPressureKPa);
        Assert.Equal(80.0, state.LF.TempLeft, precision: 3);
        Assert.Equal(90.0, state.LF.TempMiddle, precision: 3);
        Assert.Equal(100.0, state.LF.TempRight, precision: 3);
        Assert.False(state.LF.IsSurfaceTemp);
    }

    [Fact]
    public void Build_SurfaceTempDeclaredButGarbage_FallsBackToCarcass()
    {
        // Regression test for the reported "tire info displaying random numbers" bug: the legacy
        // surface-temp variable name can be present in the var-header table without ever being
        // written a real value — if that leftover memory isn't cleanly zeroed, reinterpreting it as a
        // float can produce an enormous, nonsensical reading that still passes a bare "> 0" check.
        // A physically-implausible reading must be treated the same as "not available."
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LFtempL", IrsdkVarType.Float);
        builder.AddVar("LFtempM", IrsdkVarType.Float);
        builder.AddVar("LFtempR", IrsdkVarType.Float);
        builder.AddVar("LFtempCL", IrsdkVarType.Float);
        builder.AddVar("LFtempCM", IrsdkVarType.Float);
        builder.AddVar("LFtempCR", IrsdkVarType.Float);

        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("LFtempL", float.MaxValue); // garbage/uninitialized reading
            w.SetFloat("LFtempM", float.MaxValue);
            w.SetFloat("LFtempR", float.MaxValue);
            w.SetFloat("LFtempCL", 80.0f);
            w.SetFloat("LFtempCM", 90.0f);
            w.SetFloat("LFtempCR", 100.0f);
        });

        var state = TireInfoBuilder.Build(snapshot);

        Assert.Equal(80.0, state.LF.TempLeft, precision: 3);
        Assert.Equal(90.0, state.LF.TempMiddle, precision: 3);
        Assert.Equal(100.0, state.LF.TempRight, precision: 3);
        Assert.False(state.LF.IsSurfaceTemp);
    }

    [Fact]
    public void Build_PrefersSurfaceTemp_WhenAvailable()
    {
        // Surface temp (no "C") is closer to what an in-car dash actually shows; carcass temp
        // (garage/setup-screen style) is only a fallback for cars/builds without it.
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LFtempL", IrsdkVarType.Float);
        builder.AddVar("LFtempM", IrsdkVarType.Float);
        builder.AddVar("LFtempR", IrsdkVarType.Float);
        builder.AddVar("LFtempCL", IrsdkVarType.Float);
        builder.AddVar("LFtempCM", IrsdkVarType.Float);
        builder.AddVar("LFtempCR", IrsdkVarType.Float);

        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("LFtempL", 70.0f);
            w.SetFloat("LFtempM", 75.0f);
            w.SetFloat("LFtempR", 78.0f);
            w.SetFloat("LFtempCL", 80.0f);
            w.SetFloat("LFtempCM", 90.0f);
            w.SetFloat("LFtempCR", 100.0f);
        });

        var state = TireInfoBuilder.Build(snapshot);

        Assert.Equal(70.0, state.LF.TempLeft, precision: 3);
        Assert.Equal(75.0, state.LF.TempMiddle, precision: 3);
        Assert.Equal(78.0, state.LF.TempRight, precision: 3);
        Assert.True(state.LF.IsSurfaceTemp);
    }

    [Fact]
    public void Build_SurfaceTempDeclaredButAlwaysZero_FallsBackToCarcass()
    {
        // Regression test for the reported "tire temps still not updating" bug: the earlier fix
        // gated on HasVariable(surfaceTemp) alone, so a build/car where the legacy surface-temp name
        // is still declared in the var table but never actually written to (permanently 0) would lock
        // onto three dead zeros forever instead of falling back to the live carcass values.
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LFtempL", IrsdkVarType.Float);
        builder.AddVar("LFtempM", IrsdkVarType.Float);
        builder.AddVar("LFtempR", IrsdkVarType.Float);
        builder.AddVar("LFtempCL", IrsdkVarType.Float);
        builder.AddVar("LFtempCM", IrsdkVarType.Float);
        builder.AddVar("LFtempCR", IrsdkVarType.Float);

        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            // LFtempL/M/R deliberately left at 0 (the default) — declared but dead.
            w.SetFloat("LFtempCL", 80.0f);
            w.SetFloat("LFtempCM", 90.0f);
            w.SetFloat("LFtempCR", 100.0f);
        });

        var state = TireInfoBuilder.Build(snapshot);

        Assert.Equal(80.0, state.LF.TempLeft, precision: 3);
        Assert.Equal(90.0, state.LF.TempMiddle, precision: 3);
        Assert.Equal(100.0, state.LF.TempRight, precision: 3);
        Assert.False(state.LF.IsSurfaceTemp);
    }

    [Fact]
    public void Build_MissingVariables_DoesNotThrowAndReportsUnavailable()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var state = TireInfoBuilder.Build(snapshot);

        Assert.Equal("—", state.LF.PressureDisplay);
        Assert.Equal("—", state.RF.TempLeftDisplay);
        Assert.Equal("LR", state.LR.Label);
        Assert.Equal("RR", state.RR.Label);
        Assert.False(state.LF.HasWearData);
        Assert.Equal(1.0, state.LF.WorstWearFraction);
    }

    [Fact]
    public void Build_WearReported_UsesWorstZoneAsWorstWearFraction()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LFwearL", IrsdkVarType.Float);
        builder.AddVar("LFwearM", IrsdkVarType.Float);
        builder.AddVar("LFwearR", IrsdkVarType.Float);

        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("LFwearL", 0.6f);
            w.SetFloat("LFwearM", 0.4f); // worst zone
            w.SetFloat("LFwearR", 0.7f);
        });

        var state = TireInfoBuilder.Build(snapshot);

        Assert.True(state.LF.HasWearData);
        Assert.Equal(0.4, state.LF.WorstWearFraction, precision: 3);
    }
}
