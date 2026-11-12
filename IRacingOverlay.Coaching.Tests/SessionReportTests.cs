using IRacingOverlay.Coaching.Reports;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public class SessionReportTests
{
    private static readonly TrackSimulator Track = TrackSimulator.Standard();

    private static readonly SessionReportInfo Info = new()
    {
        Id = "test",
        Source = "live",
        StartedUtc = new DateTime(2026, 11, 12, 18, 30, 0, DateTimeKind.Utc),
        TrackKey = "testtrack",
        TrackName = "Test Track",
        CarName = "Test Car",
    };

    private static CoachingSession Drive(IReadOnlyList<double[]> laps)
    {
        var session = new CoachingSession(new CoachingOptions { CornerMapLaps = 3 });
        foreach (var f in Track.Drive(laps))
        {
            session.Observe(f);
        }

        session.Flush();
        return session;
    }

    [Fact]
    public void ASlowCornerOnMostLapsIsARecurringMistake_AndTheBestLapAndTheoreticalBestAreThere()
    {
        var normal = Track.SpeedProfile();
        var slowHairpin = Track.SpeedProfile(new Dictionary<int, double> { [1] = 0.9 });
        var slowMedium = Track.SpeedProfile(new Dictionary<int, double> { [3] = 0.92 });
        // Lap 0 is partial. One good lap, the hairpin slow on five of the other six, the medium corner slow once.
        var session = Drive([normal, normal, slowHairpin, slowHairpin, slowMedium, slowHairpin, slowHairpin, slowHairpin]);

        var report = SessionReportBuilder.Build(session, Info);

        Assert.Equal(session.Laps.Count, report.Laps.Count);
        Assert.Equal(7, report.CleanLapCount);
        Assert.Equal(1, report.BestLapNumber);
        Assert.Equal(TrackSimulator.LapTime(normal), report.BestLapTime!.Value, 0.002);
        Assert.NotNull(report.TheoreticalBestLapTime);
        Assert.True(report.TheoreticalBestLapTime <= report.BestLapTime + 1e-9);
        Assert.NotNull(report.AverageGapToBest);
        Assert.Equal(3, report.Corners.Count);

        var hairpin = report.Corners[0];
        Assert.Equal("T1", hairpin.Id);
        Assert.Equal(7, hairpin.CleanPasses);
        Assert.True(hairpin.MedianLoss > 0.1);
        Assert.NotNull(hairpin.Best);
        Assert.True(hairpin.Best!.IsBest);
        Assert.NotNull(hairpin.Typical);
        Assert.NotEmpty(hairpin.Typical!.Findings);
        Assert.NotNull(hairpin.BestTrace);
        Assert.NotNull(hairpin.TypicalTrace);
        Assert.Equal(hairpin.BestTrace!.SpeedKmh.Length, hairpin.BestTrace.Brake.Length);
        Assert.True(hairpin.BestTrace.SpeedKmh.Min() < hairpin.BestTrace.SpeedKmh.Max());

        // The hairpin's slow laps are the summary's lead; the medium corner's one slow lap isn't a habit.
        var lead = Assert.Single(report.Mistakes.Where(m => m.CornerId == "T1").Take(1));
        Assert.Equal("T1", report.Mistakes[0].CornerId);
        Assert.Equal(5, lead.Laps);
        Assert.Equal(6, lead.OutOf);
        Assert.Contains("on 5 of 6 laps", lead.Problem);
        Assert.DoesNotContain(report.Mistakes, m => m.CornerId == "T2");

        var headline = SessionSummaryText.Headline(report);
        Assert.Contains(headline, l => l.StartsWith("Best lap ", StringComparison.Ordinal) && l.Contains("lap 1,"));
        Assert.Contains(headline, l => l.Contains("loses the most at T1"));
    }

    [Fact]
    public void TooFewLapsSaysSo_AndHasNoRecurringMistakes()
    {
        var normal = Track.SpeedProfile();
        var slow = Track.SpeedProfile(new Dictionary<int, double> { [1] = 0.9 });
        var report = SessionReportBuilder.Build(Drive([normal, normal, slow]), Info);

        Assert.Empty(report.Mistakes);
        Assert.Contains(report.Notes, n => n.Contains("Only 2 clean laps"));
    }

    [Fact]
    public void NoCleanLapGivesAnEmptyButValidReport()
    {
        var report = SessionReportBuilder.Build(new CoachingSession(), Info);

        Assert.Empty(report.Laps);
        Assert.Empty(report.Corners);
        Assert.Null(report.BestLapTime);
        Assert.NotEmpty(report.Notes);
        Assert.Contains(SessionSummaryText.Headline(report), l => l.StartsWith("No clean lap", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2, 6, false)] // a third of the laps
    [InlineData(3, 6, true)]
    [InlineData(2, 3, true)]
    [InlineData(2, 2, false)] // too few passes to call it a habit
    [InlineData(1, 3, false)]
    public void RecurringNeedsEnoughLapsAndShare(int laps, int outOf, bool expected) =>
        Assert.Equal(expected, SessionReportBuilder.IsRecurring(
            new FindingTally { Kind = "BrakedTooEarly", Laps = laps, OutOf = outOf, Advice = "x" }, new SessionReportOptions()));

    [Fact]
    public void AKindThisVersionDoesntKnowStillReads()
    {
        var text = SessionSummaryText.Problem(new FindingTally { Kind = "LiftTooEarly", Laps = 4, OutOf = 8, TypicalLoss = 0.1, Advice = "lift later" });

        Assert.Equal("You had lift too early on 4 of 8 laps, losing about 0.10 s each time.", text);
    }

    [Fact]
    public void StoreSavesListsNewestFirstAndDeletes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oo-reports-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SessionReportStore(dir);
            var normal = Track.SpeedProfile();
            var full = SessionReportBuilder.Build(Drive([normal, normal, normal, normal]), Info);
            var older = full with { Id = SessionReportStore.NewId(Info.StartedUtc.AddDays(-1), "spa 2024 up"), StartedUtc = Info.StartedUtc.AddDays(-1) };
            store.Save(full);
            store.Save(older);
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json");

            var all = store.LoadAll();

            Assert.Equal(["test", "20261111-183000-spa-2024-up"], all.Select(r => r.Id));
            var back = store.Load("test")!;
            Assert.Equal(full.BestLapTime, back.BestLapTime);
            Assert.Equal(full.Corners.Count, back.Corners.Count);
            Assert.Equal(full.Corners[0].BestTrace!.SpeedKmh, back.Corners[0].BestTrace!.SpeedKmh);
            Assert.Equal(full.Laps.Count, back.Laps.Count);
            Assert.True(store.Delete("test"));
            Assert.Single(store.LoadAll());
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void AnIbtFileGivesAReportNamedAfterIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oo-ibt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var normal = Track.SpeedProfile();
            var path = Path.Combine(dir, "mx5 testtrack 2026-11-12.ibt");
            File.WriteAllBytes(path, IbtWriter.Write(Track.Drive([normal, normal, normal])));

            var (_, report) = IbtSessionAnalyzer.Analyze(path, store: null);

            Assert.Equal("ibt-mx5-testtrack-2026-11-12", report.Id);
            Assert.Equal("ibt", report.Source);
            Assert.Equal("Test Track", report.TrackName);
            Assert.Equal("Test Car", report.CarName);
            Assert.Equal(2, report.CleanLapCount);
            Assert.Equal(3, report.Corners.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LiveSaverWritesTheLatestReport()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oo-live-" + Guid.NewGuid().ToString("N"));
        try
        {
            var saver = new LiveReportSaver(new SessionReportStore(dir));
            var normal = Track.SpeedProfile();
            saver.Save(Drive([normal, normal]), Info);
            saver.Save(Drive([normal, normal, normal]), Info);
            saver.Flush();

            Assert.Null(saver.LastError);
            Assert.Equal(new SessionReportStore(dir).PathFor("test"), saver.LastPath);
            Assert.Equal(2, new SessionReportStore(dir).Load("test")!.CleanLapCount);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
