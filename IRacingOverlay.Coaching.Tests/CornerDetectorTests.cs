using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public class CornerDetectorTests
{
    private static LapTrace CleanLap(TrackSimulator track)
    {
        var profile = track.SpeedProfile();
        var recorder = new LapRecorder();
        foreach (var f in track.Drive([profile, profile]))
        {
            recorder.Observe(f);
        }

        return recorder.Laps.First(l => l.IsClean).Trace!;
    }

    [Fact]
    public void FindsEveryBrakingCornerAndSkipsTheFlatKink()
    {
        var track = TrackSimulator.Standard();

        var map = CornerDetector.Detect(CleanLap(track));

        Assert.Equal(["T1", "T2", "T3"], map.Corners.Select(c => c.Name));
        // Apexes sit inside the hairpin, the medium corner and the tight corner (pieces 1, 3, 7).
        AssertInside(map.Corners[0].ApexM, track, 1);
        AssertInside(map.Corners[1].ApexM, track, 3);
        AssertInside(map.Corners[2].ApexM, track, 7);
    }

    [Fact]
    public void TheWindowStartsAtTheBrakingPointAndEndsBackOnFullThrottle()
    {
        var track = TrackSimulator.Standard();
        var hairpinStart = track.PieceStart(1);
        // Braking from top speed to the hairpin's grip limit, at the simulator's braking rate.
        var vCorner = Math.Sqrt(TrackSimulator.LateralGrip * 30);
        var brakingDistance = (TrackSimulator.TopSpeed * TrackSimulator.TopSpeed - vCorner * vCorner) / (2 * TrackSimulator.Braking);

        var t1 = CornerDetector.Detect(CleanLap(track)).Corners[0];

        Assert.InRange(t1.StartM, hairpinStart - brakingDistance - 2, hairpinStart - brakingDistance + 2);
        Assert.InRange(t1.EndM, hairpinStart + 60, hairpinStart + 70);
    }

    [Fact]
    public void ACornerWithNoRealStraightBeforeItIsStillItsOwnCorner()
    {
        // Two corners 80 m apart: the driver never reaches full throttle in between.
        var track = new TrackSimulator(
            new TrackPiece(500),
            new TrackPiece(60, 30),
            new TrackPiece(80),
            new TrackPiece(60, 40),
            new TrackPiece(700));

        var map = CornerDetector.Detect(CleanLap(track));

        Assert.Equal(2, map.Corners.Count);
        Assert.True(map.Corners[0].EndM <= map.Corners[1].StartM);
    }

    private static void AssertInside(int metre, TrackSimulator track, int piece)
    {
        var start = track.PieceStart(piece);
        Assert.InRange(metre, start, start + (int)track.Pieces[piece].Length);
    }
}
