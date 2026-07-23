using IRacingOverlay.App.ViewModels;
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
        builder.AddVar("Clutch", IrsdkVarType.Float);
        return builder;
    }

    [Fact]
    public void Build_ReadsCurrentPedalValues()
    {
        var builder = PedalVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("Throttle", 0.8f);
            w.SetFloat("Brake", 0.1f);
            w.SetFloat("Clutch", 0f); // raw 0 = fully disengaged (pedal to the floor)
        });

        var state = new PedalTraceBuilder().Build(snapshot, tickIntervalMs: 100);

        Assert.Equal(0.8, state.Throttle, precision: 3);
        Assert.Equal(0.1, state.Brake, precision: 3);
        Assert.Equal(1, state.Clutch, precision: 3); // inverted: fully pressed
    }

    [Fact]
    public void Build_ClutchReleased_ReadsAsNotPressed()
    {
        // iRacing reports raw Clutch=1 when the pedal is released (e.g. autoclutch idling) —
        // must display as 0% pressed, not 100%.
        var builder = PedalVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("Throttle", 0f);
            w.SetFloat("Brake", 0f);
            w.SetFloat("Clutch", 1f);
        });

        var state = new PedalTraceBuilder().Build(snapshot, tickIntervalMs: 100);

        Assert.Equal(0, state.Clutch, precision: 3);
    }

    [Fact]
    public void Build_AccumulatesHistoryAcrossCalls()
    {
        var builder = PedalVars();
        var pedalTraceBuilder = new PedalTraceBuilder();

        for (var i = 0; i < 5; i++)
        {
            var value = i / 10.0f;
            var snapshot = TestSnapshotFactory.Build(builder, w =>
            {
                w.SetFloat("Throttle", value);
                w.SetFloat("Brake", 0f);
                w.SetFloat("Clutch", 0f);
            });
            pedalTraceBuilder.Build(snapshot, tickIntervalMs: 100);
        }

        var final = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("Throttle", 0.9f);
            w.SetFloat("Brake", 0f);
            w.SetFloat("Clutch", 0f);
        });
        var state = pedalTraceBuilder.Build(final, tickIntervalMs: 100);

        Assert.Equal(6, state.ThrottleHistory.Count);
        Assert.Equal(0.9, state.ThrottleHistory[^1], precision: 3);
        Assert.Equal(0.0, state.ThrottleHistory[0], precision: 3);
    }

    [Fact]
    public void Build_HistoryCapsAtFixedLength()
    {
        var builder = PedalVars();
        var pedalTraceBuilder = new PedalTraceBuilder();
        PedalTraceState state = PedalTraceState.Empty;

        for (var i = 0; i < 100; i++)
        {
            var snapshot = TestSnapshotFactory.Build(builder, w =>
            {
                w.SetFloat("Throttle", 0.5f);
                w.SetFloat("Brake", 0f);
                w.SetFloat("Clutch", 0f);
            });
            state = pedalTraceBuilder.Build(snapshot, tickIntervalMs: 100);
        }

        Assert.True(state.ThrottleHistory.Count <= 50);
    }

    [Fact]
    public void Build_FasterTickInterval_KeepsSameTimeWindowWithMoreSamples()
    {
        // At a faster refresh rate, the trace must still span ~5 real seconds — capping at a fixed
        // sample COUNT regardless of tick rate would compress that window (this was the actual bug:
        // 50 samples at a 16ms/60Hz tick is under a second of history, not 5s, which read as the
        // trace stuttering/lurching every tick).
        var builder = PedalVars();
        var pedalTraceBuilder = new PedalTraceBuilder();
        PedalTraceState state = PedalTraceState.Empty;

        for (var i = 0; i < 400; i++)
        {
            var snapshot = TestSnapshotFactory.Build(builder, w =>
            {
                w.SetFloat("Throttle", 0.5f);
                w.SetFloat("Brake", 0f);
                w.SetFloat("Clutch", 0f);
            });
            state = pedalTraceBuilder.Build(snapshot, tickIntervalMs: 16);
        }

        // ~5000ms / 16ms ≈ 312 samples — comfortably more than the old fixed 50-sample cap.
        Assert.True(state.ThrottleHistory.Count > 50);
    }

    [Fact]
    public void Build_MissingVariables_DefaultsToZero()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var state = new PedalTraceBuilder().Build(snapshot, tickIntervalMs: 100);

        Assert.Equal(0, state.Throttle);
        Assert.Equal(0, state.Brake);
        Assert.Equal(0, state.Clutch);
    }
}
