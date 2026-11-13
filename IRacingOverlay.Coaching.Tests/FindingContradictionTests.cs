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
}
