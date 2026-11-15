using IRacingOverlay.Coaching.Advice;
using IRacingOverlay.Coaching.References;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public sealed class ReferenceLapTests : IDisposable
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coach-ref-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static CoachingSession Drive(IReadOnlyList<double[]> laps, ReferenceLap? reference = null, Action<LapDebrief>? debrief = null)
    {
        var session = new CoachingSession(new CoachingOptions { CornerMapLaps = 3, Reference = reference });
        if (debrief is not null)
        {
            PostLapCoach.Attach(session, debrief);
        }

        foreach (var f in Track.Drive(laps))
        {
            session.Observe(f);
        }

        session.Flush();
        return session;
    }

    private static double[] Slow(double factor) => Track.SpeedProfile(new Dictionary<int, double> { [1] = factor, [3] = factor });

    [Fact]
    public void TheBestLapEverIsKeptPerTrackAndCarAndOnlyAQuickerLapReplacesIt()
    {
        var store = new ReferenceLapStore(_dir);
        var normal = Track.SpeedProfile();
        var session = Drive([normal, Slow(0.95), normal, Slow(0.9)]);
        var fast = session.BestLap!;
        var slow = session.Analyses.Select(a => a.Lap).MaxBy(l => l.LapTime)!;

        Assert.Null(store.Load("spa", "GT3"));
        var kept = store.Offer("spa", "GT3", slow, "live");
        Assert.Equal(slow.LapTime, kept!.LapTime);
        store.Offer("spa", "GT3", fast, "live");
        store.Offer("spa", "GT3", slow, "live");

        var loaded = store.Load("spa", "GT3")!;
        Assert.Equal(fast.LapTime, loaded.LapTime, 6);
        Assert.Equal(fast.Trace!.Length, loaded.Trace.Length);
        Assert.Equal(fast.Trace.Speed[500], loaded.Trace.Speed[500]);
        Assert.Null(store.Load("spa", "MX-5"));
        var info = Assert.Single(store.List());
        Assert.Equal("GT3", info.Car);

        store.Delete("spa", "GT3");
        Assert.Null(store.Load("spa", "GT3"));
    }

    [Fact]
    public void LiveCoachingComparesWithTheReferenceUntilTheSessionBeatsIt()
    {
        var normal = Track.SpeedProfile();
        var earlier = Drive([normal, normal, normal]);
        var reference = new ReferenceLap
        {
            TrackKey = "t",
            Car = "c",
            LapTime = earlier.BestLap!.LapTime,
            RecordedUtc = DateTime.UtcNow,
            Source = "live",
            Trace = earlier.BestLap.Trace!,
        };

        // Slower laps today: the debrief is against the best ever, not today's best.
        var debriefs = new List<LapDebrief>();
        var today = Drive([Slow(0.9), Slow(0.9), Slow(0.9), Slow(0.9)], reference, debriefs.Add);

        Assert.True(today.CoachingAgainstReference);
        Assert.Equal(reference.LapTime, today.CoachingReferenceLapTime);
        var t2 = today.CornerMap!.Corners[1].Name;
        Assert.Equal(0, today.CoachingReference[t2].LapNumber);
        Assert.NotEqual(0, today.BestPasses[t2].LapNumber);
        Assert.All(debriefs, d => Assert.True(d.AgainstReferenceLap));
        Assert.Contains(debriefs, d => d.Corners.Any(c => c.Corner.Name == t2));
        Assert.Contains("off your best ever", DebriefText.Spoken(debriefs[^1]));

        // A lap quicker than the reference takes over.
        var quicker = Drive([Slow(0.9), Slow(0.9), Slow(0.9), Track.SpeedProfile(new Dictionary<int, double> { [1] = 1.03 })], reference with { LapTime = reference.LapTime + 0.5 });
        Assert.False(quicker.CoachingAgainstReference);
        Assert.Equal(quicker.BestLap!.LapTime, quicker.CoachingReferenceLapTime);
    }
}
