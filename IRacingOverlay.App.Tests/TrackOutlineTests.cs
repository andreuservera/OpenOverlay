using IRacingOverlay.App.TrackMaps;
using Xunit;

namespace IRacingOverlay.App.Tests;

public class TrackOutlineTests
{
    private const double Hz = 60;

    /// <summary>A stadium-shaped lap: two straights joined by half circles, driven anticlockwise
    /// seen from above, starting at the middle of the bottom straight heading east.</summary>
    private sealed class Stadium(double straight = 800, double radius = 200)
    {
        public double Length => 2 * straight + 2 * Math.PI * radius;

        /// <summary>Position (east, north) and heading clockwise from north at a distance into the lap.</summary>
        public (double X, double Y, double Yaw) At(double d)
        {
            d %= Length;
            var half = straight / 2;
            var arc = Math.PI * radius;
            if (d < half)
            {
                return (d, 0, Math.PI / 2);
            }

            d -= half;
            if (d < arc)
            {
                var a = d / radius; // turning left: heading goes from east to west through north
                return (half + radius * Math.Sin(a), radius - radius * Math.Cos(a), Math.PI / 2 - a);
            }

            d -= arc;
            if (d < straight)
            {
                return (half - d, 2 * radius, -Math.PI / 2);
            }

            d -= straight;
            if (d < arc)
            {
                var a = d / radius;
                return (-half - radius * Math.Sin(a), radius + radius * Math.Cos(a), -Math.PI / 2 - a);
            }

            d -= arc;
            return (-half + d, 0, Math.PI / 2);
        }
    }

    private static IEnumerable<OutlineSample> Drive(Stadium track, double laps, double speed = 50, double startD = 0,
        Func<double, double>? yawNoise = null, double speedScale = 1)
    {
        var dt = 1 / Hz;
        var total = track.Length * laps;
        var t = 100.0;
        for (var d = startD; d < startD + total; d += speed * dt)
        {
            var (_, _, yaw) = track.At(d);
            yaw += yawNoise?.Invoke(d) ?? 0;
            // iRacing's YawNorth runs -pi..pi.
            yaw = Math.IEEERemainder(yaw, 2 * Math.PI);
            yield return new OutlineSample(d % track.Length / track.Length, speed * speedScale, yaw, t, false, true);
            t += dt;
        }
    }

    private static List<TrackOutline> Learn(IEnumerable<OutlineSample> samples)
    {
        var recorder = new TrackOutlineRecorder();
        var learned = new List<TrackOutline>();
        foreach (var s in samples)
        {
            if (recorder.Observe(s, "test oval") is { } outline)
            {
                learned.Add(outline);
            }
        }

        return learned;
    }

    [Fact]
    public void ACleanLap_GivesTheTrackShape_EastRightNorthUp()
    {
        var track = new Stadium();
        // Start a little before the line, so the first crossing begins the lap.
        var learned = Learn(Drive(track, 1.3, startD: track.Length * 0.9));

        var outline = Assert.Single(learned);
        Assert.Equal(TrackOutlineRecorder.Points, outline.Count);
        Assert.InRange(outline.LengthM, track.Length * 0.99, track.Length * 1.01);
        Assert.True(outline.ClosureShare < 0.005, $"closure {outline.ClosureShare}");

        var (minX, minY, maxX, maxY) = outline.Bounds();
        Assert.InRange(maxX - minX, 1190, 1210); // straight + two radii
        Assert.InRange(maxY - minY, 395, 405);

        // Lap start is the bottom straight; a quarter of the way round, the car is on the right-hand bend.
        var start = outline.At(0);
        var quarter = outline.At(0.25);
        Assert.True(start.Y < 0, "the start is on the south straight");
        Assert.True(quarter.X > 400, "the first bend is to the east");
        Assert.True(outline.At(0.5).Y > 150, "half way round is the north straight");
    }

    [Fact]
    public void DriftFromAHeadingError_IsSpreadBack_SoTheLapCloses()
    {
        var track = new Stadium();
        // A small heading error down one straight, like slip angle, leaves a gap at the end of the lap.
        var learned = Learn(Drive(track, 1.3, startD: track.Length * 0.9, yawNoise: d => d % track.Length is > 1100 and < 1700 ? 0.03 : 0));

        var outline = Assert.Single(learned);
        Assert.InRange(outline.ClosureShare, 0.001, TrackOutlineRecorder.MaxClosureShare);
        var first = outline.At(0);
        var last = outline.At(0.999);
        Assert.True(Math.Abs(first.X - last.X) < 10 && Math.Abs(first.Y - last.Y) < 10, "the outline closes on itself");
    }

    [Fact]
    public void ALapThatDoesNotCloseAtAll_IsThrownAway()
    {
        var track = new Stadium();
        // A heading far off for a whole straight: the lap comes out a shape that can't close.
        var learned = Learn(Drive(track, 1.3, startD: track.Length * 0.9, yawNoise: d => d % track.Length is > 1100 and < 1700 ? 1.0 : 0));

        Assert.Empty(learned);
    }

    [Fact]
    public void PitRoad_OrATow_CancelsTheLap()
    {
        var track = new Stadium();
        var samples = Drive(track, 1.3, startD: track.Length * 0.9).ToList();
        var pitted = samples.Select((s, i) => i == samples.Count / 2 ? s with { OnPitRoad = true } : s);
        Assert.Empty(Learn(pitted));

        var towed = samples.Select((s, i) => i > samples.Count / 2 ? s with { LapDistPct = Math.Min(0.999, s.LapDistPct + 0.2) } : s);
        Assert.Empty(Learn(towed));
    }

    [Fact]
    public void APause_DoesNotCountAsAGap()
    {
        var track = new Stadium();
        var samples = Drive(track, 1.3, startD: track.Length * 0.9).ToList();
        var paused = new List<OutlineSample>();
        for (var i = 0; i < samples.Count; i++)
        {
            paused.Add(samples[i]);
            if (i == samples.Count / 2)
            {
                // Same tick over and over while the sim is paused.
                paused.AddRange(Enumerable.Repeat(samples[i], 300));
            }
        }

        Assert.Single(Learn(paused));
    }

    [Fact]
    public void Progress_FollowsTheLapBeingLearned()
    {
        var track = new Stadium();
        var recorder = new TrackOutlineRecorder();
        Assert.Null(recorder.Progress);
        foreach (var s in Drive(track, 0.6, startD: track.Length * 0.9))
        {
            recorder.Observe(s, "test oval");
        }

        Assert.InRange(recorder.Progress ?? -1, 0.45, 0.55);
    }

    [Fact]
    public void Store_RoundTripsAnOutline()
    {
        var directory = Path.Combine(Path.GetTempPath(), "oo-trackmaps-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TrackOutlineStore(directory);
            var outline = Assert.Single(Learn(Drive(new Stadium(), 1.3, startD: new Stadium().Length * 0.9)));
            outline = outline with { Track = "spa up" };

            Assert.Null(store.Load("spa up"));
            store.Save(outline);
            var loaded = store.Load("spa up");

            Assert.NotNull(loaded);
            Assert.Equal(outline.X, loaded.X);
            Assert.Equal(outline.Y, loaded.Y);
            Assert.Equal(outline.LengthM, loaded.LengthM);
            Assert.EndsWith("spa-up.json", store.PathFor("spa up"));
            Assert.True(store.Delete("spa up"));
            Assert.Null(store.Load("spa up"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
