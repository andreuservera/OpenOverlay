using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Telemetry;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public class LapRecorderTests
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();

    private static List<RecordedLap> Record(IEnumerable<TelemetryFrame> frames, double? trackLength = null)
    {
        var recorder = new LapRecorder(trackLength);
        foreach (var f in frames)
        {
            recorder.Observe(f);
        }

        recorder.Flush();
        return recorder.Laps.ToList();
    }

    private static List<TelemetryFrame> Laps(int count)
    {
        var profile = Track.SpeedProfile();
        return Track.Drive(Enumerable.Repeat(profile, count + 1).ToList());
    }

    [Fact]
    public void LapTimesMatchTheDrivenTimeToTheMillisecond()
    {
        var expected = TrackSimulator.LapTime(Track.SpeedProfile());

        var laps = Record(Laps(3));

        Assert.Equal(4, laps.Count); // the partial lap the recording began in, then three
        Assert.True(laps[0].Flaws.HasFlag(LapFlaw.Partial));
        Assert.Null(laps[0].Trace);
        foreach (var lap in laps.Skip(1))
        {
            Assert.True(lap.IsClean, lap.Flaws.ToString());
            Assert.Equal(expected, lap.LapTime, 0.001);
        }
    }

    [Fact]
    public void ACrossingWithLapDistPctPastOneStaysBetweenTheTwoTicks()
    {
        // In the pit lane iRacing lets LapDistPct run slightly past 1 before it wraps (seen at
        // Tsukuba: 1.000337 then 0.000469); the crossing must still land between those ticks.
        var frames = Laps(3);
        var crossings = 0;
        for (var i = 1; i < frames.Count; i++)
        {
            // Only one crossing, so the error doesn't cancel out between a lap's two ends.
            if (frames[i - 1].LapDistPct > 0.75f && frames[i].LapDistPct < 0.25f && ++crossings == 2)
            {
                frames[i - 1] = frames[i - 1] with { LapDistPct = 1.0003f, LapDist = (float)Track.Length + 0.6f };
                frames[i] = frames[i] with { LapDistPct = 0.0004f, LapDist = 0.8f };
            }
        }

        var expected = TrackSimulator.LapTime(Track.SpeedProfile());
        foreach (var lap in Record(frames).Skip(1))
        {
            Assert.Equal(expected, lap.LapTime, 1.0 / 60);
        }
    }

    [Fact]
    public void TheTraceIsOnAOneMetreGridWithTimeRunningFromTheLine()
    {
        var lap = Record(Laps(1))[1];
        var trace = lap.Trace!;

        Assert.InRange(trace.Length, Track.Length - 1, Track.Length);
        Assert.InRange(trace.Time[0], 0, 0.05);
        Assert.Equal(lap.LapTime, trace.Time[^1], 0.05);
        for (var m = 1; m < trace.Length; m++)
        {
            Assert.True(trace.Time[m] > trace.Time[m - 1], $"time must increase at {m} m");
        }

        // Grid speed matches the simulated profile.
        var profile = Track.SpeedProfile();
        Assert.Equal(profile[330], trace.Speed[330], 0.2);
    }

    [Fact]
    public void LearnsTheTrackLengthWhenNotGiven()
    {
        var recorder = new LapRecorder();
        foreach (var f in Laps(1))
        {
            recorder.Observe(f);
        }

        Assert.Equal(Track.Length, recorder.TrackLength, 0.5);
    }

    [Theory]
    [InlineData(LapFlaw.PitRoad)]
    [InlineData(LapFlaw.OffTrack)]
    [InlineData(LapFlaw.Incident)]
    [InlineData(LapFlaw.NotRacing)]
    public void ALapWithAFlawIsNotClean(LapFlaw flaw)
    {
        var frames = Laps(3).Select(f => f.Lap == 2 && f.LapDistPct is > 0.4f and < 0.45f ? Spoil(f, flaw) : f).ToList();
        if (flaw == LapFlaw.Incident)
        {
            // the count stays up for the rest of the session
            frames = frames.Select(f => f.Lap > 2 || (f.Lap == 2 && f.LapDistPct > 0.4f) ? f with { IncidentCount = 1 } : f).ToList();
        }

        var laps = Record(frames);

        Assert.True(laps[1].IsClean);
        Assert.True(laps[2].Flaws.HasFlag(flaw), laps[2].Flaws.ToString());
        // Leaving the racing states (cool down) makes the next lap a start lap; the rest are local.
        Assert.Equal(flaw == LapFlaw.NotRacing ? LapFlaw.StartLap : LapFlaw.None, laps[3].Flaws);
    }

    [Fact]
    public void TheLapAfterTimeOutsideRacingIsAStartLap()
    {
        var frames = Laps(3).Select(f => f.Lap <= 1 ? f with { SessionState = 3 } : f).ToList();

        var laps = Record(frames);

        Assert.True(laps[1].Flaws.HasFlag(LapFlaw.NotRacing));
        Assert.Equal(LapFlaw.StartLap, laps[2].Flaws);
        Assert.True(laps[3].IsClean);
    }

    [Fact]
    public void AGapInTheTelemetryMarksTheLap()
    {
        var frames = Laps(2);
        var cut = frames.FindIndex(f => f.Lap == 1 && f.LapDistPct > 0.5f);
        frames.RemoveRange(cut, 120); // two seconds missing

        var laps = Record(frames);

        Assert.True(laps[1].Flaws.HasFlag(LapFlaw.TelemetryGap));
        Assert.True(laps[2].IsClean);
    }

    [Fact]
    public void ANewSessionStartsOver()
    {
        var first = Laps(1);
        var second = Laps(2).Select(f => f with { SessionNum = 1, SessionTime = f.SessionTime + 1000 });

        var laps = Record(first.Concat(second));

        var session1 = laps.Where(l => l.SessionNum == 1).ToList();
        Assert.True(session1[0].Flaws.HasFlag(LapFlaw.Partial));
        Assert.True(session1.Skip(1).All(l => l.IsClean));
    }

    [Fact]
    public void PicksUpIRacingsOwnLapTimeAfterTheLine()
    {
        var expected = TrackSimulator.LapTime(Track.SpeedProfile());
        var frames = Laps(2);
        // iRacing publishes the last lap time a few ticks after the line.
        var crossings = new List<int>();
        for (var i = 1; i < frames.Count; i++)
        {
            if (frames[i].Lap != frames[i - 1].Lap)
            {
                crossings.Add(i);
            }
        }

        var value = 0f;
        var next = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            if (next < crossings.Count && i == crossings[next] + 5)
            {
                value = next == 0 ? 33.3f : (float)expected;
                next++;
            }

            frames[i] = frames[i] with { LapLastLapTime = value };
        }

        var laps = Record(frames);

        Assert.Equal(expected, laps[1].ReportedLapTime!.Value, 0.001);
    }

    private static TelemetryFrame Spoil(TelemetryFrame f, LapFlaw flaw) => flaw switch
    {
        LapFlaw.PitRoad => f with { OnPitRoad = true },
        LapFlaw.OffTrack => f with { TrackSurface = TrackSurface.OffTrack },
        LapFlaw.Incident => f with { IncidentCount = 1 },
        LapFlaw.NotRacing => f with { SessionState = 6 },
        _ => f,
    };
}
