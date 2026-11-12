using IRacingOverlay.Coaching.Ibt;
using IRacingOverlay.Coaching.Telemetry;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public class IbtFileTests
{
    private static List<TelemetryFrame> SampleFrames() =>
    [
        new() { SessionTime = 10.0, Lap = 3, LapDist = 100.5f, LapDistPct = 0.25f, Speed = 50f, Throttle = 1f, Gear = 4, IsOnTrack = true, TrackSurface = TrackSurface.OnTrack, Latitude = 50.4372, Longitude = 5.9714 },
        new() { SessionTime = 10.0 + 1 / 60.0, Lap = 3, LapDist = 101.3f, LapDistPct = 0.2502f, Speed = 49.5f, Brake = 0.8f, AbsActive = true, OnPitRoad = true, IncidentCount = 2, IsOnTrack = true, TrackSurface = TrackSurface.OffTrack },
    ];

    [Fact]
    public void ReadsHeaderSessionAndEveryRecord()
    {
        var frames = SampleFrames();
        using var ibt = IbtFile.Open(new MemoryStream(IbtWriter.Write(frames)));

        Assert.Equal(60, ibt.TickRate);
        Assert.Equal(2, ibt.RecordCount);
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc), ibt.SessionStartUtc);
        Assert.Equal("Test Track", ibt.Session?.WeekendInfo?.TrackDisplayName);
        Assert.True(ibt.HasVariable("LapDist"));

        var read = ibt.Frames().ToList();
        Assert.Equal(2, read.Count);
        Assert.Equal(10.0, read[0].SessionTime);
        Assert.Equal(3, read[0].Lap);
        Assert.Equal(100.5f, read[0].LapDist);
        Assert.Equal(4, read[0].Gear);
        Assert.Equal(50.4372, read[0].Latitude);
        Assert.Equal(0.8f, read[1].Brake);
        Assert.True(read[1].AbsActive);
        Assert.True(read[1].OnPitRoad);
        Assert.Equal(2, read[1].IncidentCount);
        Assert.Equal(TrackSurface.OffTrack, read[1].TrackSurface);
    }

    [Fact]
    public void RawRecordsReadAnyChannelByName()
    {
        using var ibt = IbtFile.Open(new MemoryStream(IbtWriter.Write(SampleFrames())));

        var record = ibt.ReadRecord(1);

        Assert.Equal(49.5, record.Get("Speed"), 3);
        Assert.Equal(1, record.Get("OnPitRoad"));
        Assert.Throws<ArgumentException>(() => record.Get("NoSuchChannel"));
    }

    [Fact]
    public void ATruncatedFileReadsUpToItsLastWholeRecord()
    {
        var bytes = IbtWriter.Write(SampleFrames());
        var cut = bytes[..^5]; // the second record loses its tail

        using var ibt = IbtFile.Open(new MemoryStream(cut));

        Assert.Equal(1, ibt.RecordCount);
        Assert.Single(ibt.Frames());
    }

    [Fact]
    public void RejectsAFileThatIsNotTelemetry()
    {
        var junk = new byte[400];
        Assert.Throws<InvalidDataException>(() => IbtFile.Open(new MemoryStream(junk)));
    }

    [Fact]
    public void AMissingChannelReadsAsDefault()
    {
        // The writer doesn't include Clutch or RPM; frames still decode.
        using var ibt = IbtFile.Open(new MemoryStream(IbtWriter.Write(SampleFrames())));
        var frame = ibt.Frames().First();
        Assert.Equal(0f, frame.Clutch);
        Assert.Equal(0f, frame.Rpm);
    }
}
