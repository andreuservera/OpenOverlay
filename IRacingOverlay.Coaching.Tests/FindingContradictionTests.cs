using IRacingOverlay.Coaching.History;
using IRacingOverlay.Coaching.Reports;

namespace IRacingOverlay.Coaching.Tests;

public sealed class FindingContradictionTests
{
    private static FindingTally Tally(string kind, int laps, int outOf = 8) =>
        new() { Kind = kind, Laps = laps, OutOf = outOf, TypicalAmount = 12, TypicalLoss = 0.1, Advice = kind };

    [Fact]
    public void OppositeAdviceOnOneLapEach_IsLeftOut()
    {
        var kept = FindingContradictions.Without([Tally("BrakedTooEarly", 1, 2), Tally("BrakedTooLate", 1, 2), Tally("LateThrottle", 2, 2)]);

        Assert.Equal(["LateThrottle"], kept.Select(f => f.Kind));
    }

    [Fact]
    public void AClearlyStrongerSide_Stays()
    {
        var kept = FindingContradictions.Without([Tally("BrakedTooEarly", 5), Tally("BrakedTooLate", 1)]);

        Assert.Equal(["BrakedTooEarly"], kept.Select(f => f.Kind));
    }

    [Fact]
    public void ADriveSavedTwice_IsOneEntry_ShowingTheIbt()
    {
        var start = new DateTime(2026, 11, 13, 11, 45, 0, DateTimeKind.Utc);
        SessionReport Report(string id, string source, DateTime at) => new()
        {
            Id = id,
            Source = source,
            StartedUtc = at,
            TrackKey = "spa",
            CarName = "992",
            BestLapTime = 137.46,
            Laps = [new LapRow { Number = 1, Time = 137.46, Clean = true }],
        };

        var drives = TrackHistory.Drives([Report("live", "live", start), Report("ibt", "ibt", start.AddMinutes(5)), Report("other", "live", start.AddDays(1))]);

        Assert.Equal(2, drives.Count);
        Assert.Equal("ibt", drives[0].Kept.Id);
        Assert.Equal(["live"], drives[0].Copies.Select(r => r.Id));
        Assert.Empty(drives[1].Copies);
    }

    [Fact]
    public void DrivesCloseInTime_StayApart_AndASplitLiveReportJoinsItsDrive()
    {
        var day = new DateTime(2026, 11, 13, 11, 24, 0, DateTimeKind.Utc);
        SessionReport Report(string id, string source, int minutes, params double[] laps) => new()
        {
            Id = id,
            Source = source,
            StartedUtc = day.AddMinutes(minutes),
            TrackKey = "spa",
            CarName = "992",
            BestLapTime = laps.Where(t => t < 200).Min(),
            Laps = laps.Select((t, i) => new LapRow { Number = i + 1, Time = t, Clean = t < 200 }).ToList(),
        };

        // 12:24: an .ibt with a long out lap, and the live coach split in two by a toggle.
        var ibt1 = Report("ibt-12-24", "ibt", 0, 900, 138.112, 137.904, 138.551, 137.733);
        var live1 = Report("live-12-24", "live", 2, 138.115, 137.906);
        var live2 = Report("live-12-31", "live", 7, 138.548, 137.735);
        // 12:45 live and 12:50 .ibt of the next drive, which the first .ibt with its out lap overlaps in time.
        var live3 = Report("live-12-45", "live", 21, 137.712, 137.460);
        var ibt2 = Report("ibt-12-50", "ibt", 26, 1500, 137.711, 137.462);

        var drives = TrackHistory.Drives([ibt1, live1, live2, live3, ibt2]);

        Assert.Equal(["ibt-12-24", "ibt-12-50"], drives.Select(d => d.Kept.Id));
        Assert.Equal(["live-12-24", "live-12-31"], drives[0].Copies.Select(r => r.Id).Order());
        Assert.Equal(["live-12-45"], drives[1].Copies.Select(r => r.Id));
    }
}
