using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public class CornerAnalyzerTests
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();

    private static LapTrace Lap(IReadOnlyDictionary<int, double>? cornerSpeed = null)
    {
        var profile = Track.SpeedProfile(cornerSpeed);
        var recorder = new LapRecorder();
        foreach (var f in Track.Drive([profile, profile]))
        {
            recorder.Observe(f);
        }

        return recorder.Laps.First(l => l.IsClean).Trace!;
    }

    [Fact]
    public void MeasuresBrakingPointMinimumSpeedAndThrottle()
    {
        var lap = Lap();
        var map = CornerDetector.Detect(lap);

        var t1 = CornerAnalyzer.Analyze(lap, map, lapNumber: 1)[0];

        Assert.InRange(t1.BrakePointM!.Value, map.Corners[0].StartM, map.Corners[0].StartM + 2);
        Assert.Equal(Math.Sqrt(TrackSimulator.LateralGrip * 30), t1.MinSpeed, 0.3);
        Assert.InRange(t1.MinSpeedAtM, Track.PieceStart(1), Track.PieceStart(1) + 60);
        Assert.True(t1.PeakBrake > 0.9f);
        Assert.NotNull(t1.ThrottlePickupM);
        Assert.True(t1.ThrottlePickupM > t1.BrakeReleaseM);
        Assert.True(t1.ExitSpeedPlus100 > t1.ExitSpeed);
    }

    [Fact]
    public void ASlowerCornerTakesLongerAndIsSlowerAtTheApex()
    {
        var reference = Lap();
        var slow = Lap(new Dictionary<int, double> { [3] = 0.9 }); // medium corner 10% slower
        var map = CornerDetector.Detect(reference);

        var fast = CornerAnalyzer.Analyze(reference, map, 1);
        var slower = CornerAnalyzer.Analyze(slow, map, 2);

        Assert.True(slower[1].SegmentTime > fast[1].SegmentTime + 0.1);
        Assert.True(slower[1].MinSpeed < fast[1].MinSpeed - 1);
        Assert.True(slower[1].BrakePointM < fast[1].BrakePointM, "a slower apex needs an earlier brake point");
        Assert.Equal(fast[0].SegmentTime, slower[0].SegmentTime, 0.01);
    }
}
