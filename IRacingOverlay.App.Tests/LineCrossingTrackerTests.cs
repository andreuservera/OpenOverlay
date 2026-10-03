using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class LineCrossingTrackerTests
{
    private static readonly IracingSessionInfo Session = new()
    {
        DriverInfo = new DriverInfoSection { DriverCarIdx = 0, Drivers = [new DriverEntry { CarIdx = 0 }, new DriverEntry { CarIdx = 1 }] },
    };

    private static TelemetrySnapshot Tick(double time, int[] lapsCompleted, float[] pct, int sessionNum = 0)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("SessionNum", IrsdkVarType.Int);
        builder.AddVar("SessionTime", IrsdkVarType.Double);
        builder.AddVar("CarIdxLap", IrsdkVarType.Int, count: 2);
        builder.AddVar("CarIdxLapCompleted", IrsdkVarType.Int, count: 2);
        builder.AddVar("CarIdxLapDistPct", IrsdkVarType.Float, count: 2);
        return TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("SessionNum", sessionNum);
            w.SetDouble("SessionTime", time);
            w.SetIntArray("CarIdxLapCompleted", lapsCompleted);
            w.SetIntArray("CarIdxLap", lapsCompleted.Select(l => l + 1).ToArray());
            w.SetFloatArray("CarIdxLapDistPct", pct);
        });
    }

    [Fact]
    public void PlacesTheCrossingBetweenTheTicksEitherSideOfTheLine()
    {
        // 2% of the lap to go at 99.9s, 1% into the next at 100.0s: two thirds of the way between.
        var tracker = new LineCrossingTracker();
        tracker.Update(Tick(99.9, [4, 0], [0.98f, 0.5f]), Session);
        tracker.Update(Tick(100.0, [5, 0], [0.01f, 0.5f]), Session);

        Assert.Equal(5, tracker.LastCrossing(0)!.Value.Lap);
        Assert.Equal(99.9667, tracker.LastCrossing(0)!.Value.Time, precision: 3);
    }

    [Fact]
    public void FirstCrossing_IsTheEarliestOfTheGroup()
    {
        var tracker = new LineCrossingTracker();
        tracker.Update(Tick(99.9, [4, 4], [0.99f, 0.98f]), Session);
        tracker.Update(Tick(100.0, [5, 4], [0.0f, 0.99f]), Session);
        tracker.Update(Tick(100.1, [5, 5], [0.01f, 0.0f]), Session);

        Assert.Equal(100.0, tracker.FirstCrossing([0, 1], 5)!.Value, precision: 3);
        Assert.Null(tracker.FirstCrossing([0, 1], 6));
    }

    [Fact]
    public void ALapAlreadyCompletedWhenFirstSeen_HasNoTrustworthyFirstCrossing()
    {
        // Car 0 had completed lap 5 before we were watching: car 1 completing it is not the first.
        var tracker = new LineCrossingTracker();
        tracker.Update(Tick(100.1, [5, 4], [0.01f, 0.99f]), Session);
        tracker.Update(Tick(100.2, [5, 5], [0.02f, 0.01f]), Session);

        Assert.NotNull(tracker.LastCrossing(1));
        Assert.Null(tracker.FirstCrossing([0, 1], 5));
    }

    [Fact]
    public void ANewSession_StartsTheTimingOver()
    {
        var tracker = new LineCrossingTracker();
        tracker.Update(Tick(99.9, [4, 0], [0.99f, 0.5f]), Session);
        tracker.Update(Tick(100.0, [5, 0], [0.0f, 0.5f]), Session);
        tracker.Update(Tick(5.0, [0, 0], [0.1f, 0.1f], sessionNum: 1), Session);

        Assert.Null(tracker.LastCrossing(0));
    }

    [Fact]
    public void BackFromOutOfTheWorld_IsNotTimedAsACrossing()
    {
        // Towed: out of the world, then back a lap on. When it crossed is unknown.
        var tracker = new LineCrossingTracker();
        tracker.Update(Tick(50.0, [4, 0], [0.5f, 0.5f]), Session);
        tracker.Update(Tick(60.0, [-1, 0], [-1f, 0.5f]), Session);
        tracker.Update(Tick(130.0, [5, 0], [0.1f, 0.5f]), Session);

        Assert.Null(tracker.LastCrossing(0));
    }
}
