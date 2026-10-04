using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class PedalTraceBuilderTests
{
    private static SyntheticMemoryBuilder PedalVars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Throttle", IrsdkVarType.Float);
        builder.AddVar("Brake", IrsdkVarType.Float);
        builder.AddVar("ClutchRaw", IrsdkVarType.Float);
        builder.AddVar("Clutch", IrsdkVarType.Float);
        return builder;
    }

    // Clutch values as iRacing sends them (1 = pedal up). clutchApplied is the physics' clutch, assists
    // included; left out, it follows the pedal, as when no assist is intervening.
    private static TelemetrySnapshot Pedals(SyntheticMemoryBuilder builder, int tick, float throttle, float brake = 0, float clutchPedal = 1, float? clutchApplied = null) =>
        TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("Throttle", throttle);
            w.SetFloat("Brake", brake);
            w.SetFloat("ClutchRaw", clutchPedal);
            w.SetFloat("Clutch", clutchApplied ?? clutchPedal);
        }, tick);

    [Fact]
    public void Build_ReadsCurrentPedalValues()
    {
        var state = new PedalTraceBuilder().Build(Pedals(PedalVars(), 1, 0.8f, 0.1f, clutchPedal: 0f));

        Assert.Equal(0.8, state.Throttle, precision: 3);
        Assert.Equal(0.1, state.Brake, precision: 3);
        Assert.Equal(1, state.Clutch, precision: 3); // raw 0 = pedal to the floor
    }

    [Fact]
    public void Build_ClutchReleased_ReadsAsNotPressed()
    {
        var state = new PedalTraceBuilder().Build(Pedals(PedalVars(), 1, 0f, 0f, clutchPedal: 1f));

        Assert.Equal(0, state.Clutch, precision: 3);
    }

    [Fact]
    public void Build_AssistWorksClutch_PedalUntouched_ReadsZero()
    {
        // Anti-stall pulling the clutch as the car stops, then the start assist feeding it back in.
        var builder = PedalVars();
        var trace = new PedalTraceBuilder();
        var state = PedalTraceState.Empty;
        var tick = 0;
        foreach (var applied in new[] { 1f, 0.4f, 0f, 0f, 0.3f, 0.8f, 1f })
        {
            state = trace.Build(Pedals(builder, ++tick, 0f, clutchPedal: 1f, clutchApplied: applied));
        }

        Assert.Equal(0, state.Clutch);
        Assert.Equal(7, state.ClutchHistory.Count);
        Assert.All(state.ClutchHistory, clutch => Assert.Equal(0, clutch));
    }

    [Fact]
    public void Build_PedalPressedWhileAssistHoldsClutch_ReadsPedalOnly()
    {
        var state = new PedalTraceBuilder().Build(Pedals(PedalVars(), 1, 0f, clutchPedal: 0.75f, clutchApplied: 0f));

        Assert.Equal(0.25, state.Clutch, precision: 3);
        Assert.Equal(0.25, state.ClutchHistory[^1], precision: 3);
    }

    [Fact]
    public void Build_NoRawClutchChannel_NeverFallsBackToAssistedClutch()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Clutch", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Clutch", 0f));

        var state = new PedalTraceBuilder().Build(snapshot);

        Assert.Equal(0, state.Clutch);
        Assert.All(state.ClutchHistory, clutch => Assert.Equal(0, clutch));
    }

    [Fact]
    public void Build_AddsOneSamplePerSimTick()
    {
        var builder = PedalVars();
        var trace = new PedalTraceBuilder();

        for (var tick = 1; tick <= 5; tick++)
        {
            trace.Build(Pedals(builder, tick, (tick - 1) / 10f));
        }

        var state = trace.Build(Pedals(builder, 6, 0.9f));

        Assert.Equal(6, state.ThrottleHistory.Count);
        Assert.Equal(0.9, state.ThrottleHistory[^1], precision: 3);
        Assert.Equal(0.0, state.ThrottleHistory[0], precision: 3);
    }

    [Fact]
    public void Build_SameSimTick_AddsNothing()
    {
        // The UI can run faster than the sim publishes; repeating a tick must not stretch the trace.
        var builder = PedalVars();
        var trace = new PedalTraceBuilder();

        var first = trace.Build(Pedals(builder, 10, 0.5f));
        var again = trace.Build(Pedals(builder, 10, 0.5f));

        Assert.Same(first, again);
        Assert.Single(again.ThrottleHistory);
    }

    [Fact]
    public void Build_KeepsFiveSecondsOfSimTicks_PositionedByTick()
    {
        var builder = PedalVars();
        var trace = new PedalTraceBuilder();
        var state = PedalTraceState.Empty;

        // Every other tick missing, as when the UI lags: positions still follow the sim clock.
        for (var tick = 0; tick <= 1000; tick += 2)
        {
            state = trace.Build(Pedals(builder, tick, 0.5f));
        }

        Assert.Equal(151, state.ThrottleHistory.Count); // 300 ticks (5 s) / 2 + the newest
        Assert.Equal(1.0, state.Positions[^1], precision: 6);
        Assert.Equal(0.0, state.Positions[0], precision: 6);
        Assert.Equal(1.0 - (2 / 300.0), state.Positions[^2], precision: 6);
    }

    [Fact]
    public void Build_TickGoesBackwards_StartsAFreshTrace()
    {
        var builder = PedalVars();
        var trace = new PedalTraceBuilder();
        trace.Build(Pedals(builder, 500, 0.5f));
        trace.Build(Pedals(builder, 501, 0.5f));

        var state = trace.Build(Pedals(builder, 3, 0.2f));

        Assert.Single(state.ThrottleHistory);
    }

    [Fact]
    public void Build_MissingVariables_DefaultsToZero()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var state = new PedalTraceBuilder().Build(snapshot);

        Assert.Equal(0, state.Throttle);
        Assert.Equal(0, state.Brake);
        Assert.Equal(0, state.Clutch);
    }

    [Fact]
    public void Build_TracksAbsPerSample_AlignedWithBrakeHistory()
    {
        var builder = PedalVars();
        builder.AddVar("BrakeABSactive", IrsdkVarType.Bool);
        var trace = new PedalTraceBuilder();
        var state = PedalTraceState.Empty;

        // ABS only intervenes on the middle sample of three — only that stretch is recoloured.
        var tick = 0;
        foreach (var absActive in new[] { false, true, false })
        {
            var snapshot = TestSnapshotFactory.Build(builder, w =>
            {
                w.SetFloat("Brake", 0.9f);
                w.SetBool("BrakeABSactive", absActive);
            }, ++tick);
            state = trace.Build(snapshot);
        }

        Assert.Equal(state.BrakeHistory.Count, state.AbsHistory.Count);
        Assert.Equal(state.BrakeHistory.Count, state.ClutchHistory.Count);
        Assert.Equal(new[] { false, true, false }, state.AbsHistory);
    }

    [Fact]
    public void Build_NoAbsVariable_ReportsAbsInactive()
    {
        var state = new PedalTraceBuilder().Build(Pedals(PedalVars(), 1, 0f, 1f));

        Assert.All(state.AbsHistory, abs => Assert.False(abs));
    }

    [Fact]
    public void Build_ReadsGearSpeedAndSteering()
    {
        var builder = PedalVars();
        builder.AddVar("Gear", IrsdkVarType.Int);
        builder.AddVar("Speed", IrsdkVarType.Float);
        builder.AddVar("SteeringWheelAngle", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Gear", 3);
            w.SetFloat("Speed", 128 / 3.6f);
            w.SetFloat("SteeringWheelAngle", (float)(Math.PI / 2)); // a quarter turn left
        }, 1);

        var state = new PedalTraceBuilder().Build(snapshot);

        Assert.Equal("3", state.Gear);
        Assert.Equal("128", state.SpeedDisplay);
        Assert.Equal(-90, state.SteeringIconAngle, precision: 3); // anticlockwise on screen
    }

    [Fact]
    public void Build_WithoutGearSpeedOrSteering_ShowsDashesAndAStraightWheel()
    {
        var state = new PedalTraceBuilder().Build(Pedals(PedalVars(), 1, 0.5f));

        Assert.Equal("–", state.Gear);
        Assert.Equal("—", state.SpeedDisplay);
        Assert.Equal(0, state.SteeringIconAngle);
    }

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(0.064, "6")]
    [InlineData(1.0, "100")]
    [InlineData(1.4, "100")]
    public void Percent_IsTheWholePedalPercentage(double pedal, string expected) =>
        Assert.Equal(expected, PedalTraceState.Percent(pedal));
}
