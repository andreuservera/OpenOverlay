using IRacingOverlay.Coaching.Advice;
using IRacingOverlay.Coaching.History;
using IRacingOverlay.Coaching.Reports;
using IRacingOverlay.Coaching.Tests.Support;

namespace IRacingOverlay.Coaching.Tests;

public sealed class HistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coach-focus-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static CornerReport Corner(string id, int apex, double best, double loss, int early = 0, int outOf = 8) => new()
    {
        Id = id,
        Name = id,
        ApexM = apex,
        CleanPasses = outOf + 1,
        BestTime = best,
        MedianLoss = loss,
        Passes = Enumerable.Range(1, outOf + 1).Select(l => new PassRow { Lap = l, Clean = true, Loss = l == 1 ? 0 : loss, IsBest = l == 1 }).ToList(),
        Findings = early == 0 ? [] : [new FindingTally { Kind = "BrakedTooEarly", Laps = early, OutOf = outOf, TypicalAmount = 10, TypicalLoss = loss, Advice = "Brake 10 metres later" }],
    };

    private static SessionReport Session(int day, string car, params CornerReport[] corners) => new()
    {
        Id = $"s{day}-{car}",
        Source = "ibt",
        StartedUtc = new DateTime(2026, 11, day, 10, 0, 0, DateTimeKind.Utc),
        TrackKey = "suzuka",
        CarName = car,
        BestLapTime = 130 - day * 0.1,
        Laps = [new LapRow { Number = 1, Time = 130, Clean = true }],
        Corners = corners.ToList(),
    };

    [Fact]
    public void HistoryMatchesCornersAcrossSessionsAndShowsTheTrend()
    {
        var reports = new[]
        {
            Session(1, "296", Corner("T1", 400, 10.2, 0.40, early: 6), Corner("T2", 900, 8.0, 0.10)),
            Session(2, "296", Corner("T1", 420, 10.1, 0.30, early: 4), Corner("T2", 905, 7.9, 0.10)),
            // A relearned map: T1 moved 30 m but is the same corner.
            Session(3, "296", Corner("T1", 430, 10.0, 0.15, early: 2), Corner("T2", 900, 8.1, 0.12)),
            Session(4, "Porsche", Corner("T1", 400, 11.0, 1.0)),
        };

        var h = TrackHistory.Build(reports, "suzuka", "296")!;

        Assert.Equal(3, h.Sessions.Count);
        var t1 = h.Corners[0];
        Assert.Equal(3, t1.Points.Count);
        Assert.Equal(10.0, t1.BestEver);
        Assert.Equal(0.30, t1.RecentLoss!.Value, 6); // median of the three
        Assert.Equal(0.35, t1.EarlyLoss!.Value, 6);  // median of the first two
        Assert.Equal("BrakedTooEarly", t1.Habit!.Kind);
        Assert.Equal(0.75, t1.HabitShareFirst!.Value, 6);
        Assert.Equal(0.25, t1.HabitShareLast!.Value, 6);
        Assert.Null(TrackHistory.Build(reports, "spa", "296"));
    }

    [Fact]
    public void TheFocusPlanPicksTheCornersWithMostToGainUnlessSomeArePinned()
    {
        var h = TrackHistory.Build([Session(1, "296", Corner("T1", 400, 10, 0.32, early: 5), Corner("T2", 900, 8, 0.02), Corner("T3", 1500, 9, 0.2))], "suzuka", "296")!;

        // T3 loses time but with no mistake to name: T1, which has one, is all the plan needs.
        var plan = FocusPlanner.Plan(h);
        Assert.Equal(["T1"], plan.Select(p => p.CornerId));
        Assert.Equal(0.15, plan[0].Target!.Value, 6);
        Assert.Equal(0.17, plan[0].AimLoss!.Value, 6);
        Assert.Equal("T1: brake 10 metres later, aim to lose under 0.17 s here", plan[0].Text);

        // With no mistake anywhere (reports saved before findings carried advice), no plan rather
        // than "compare your traces".
        var none = TrackHistory.Build([Session(1, "296", Corner("T1", 400, 10, 0.32), Corner("T3", 1500, 9, 0.2))], "suzuka", "296")!;
        Assert.Empty(FocusPlanner.Plan(none));

        var store = new FocusStore(_dir);
        store.Set("suzuka", "296", "T2", "T2", 905, true);
        var pinned = FocusPlanner.Plan(h, store.Pinned("suzuka", "296"));
        Assert.True(Assert.Single(pinned).Pinned);
        Assert.Equal("T2", pinned[0].CornerId);
        store.Set("suzuka", "296", "T2", "T2", 905, false);
        Assert.Empty(store.Pinned("suzuka", "296"));
    }

    [Fact]
    public void TheSameDriveSavedLiveAndFromItsIbt_CountsOnce()
    {
        var live = Session(1, "296", Corner("T1", 400, 10, 0.3, early: 4)) with { Id = "live", Source = "live" };
        // The .ibt starts a few minutes later (the recording begins in the car) but overlaps the drive.
        var ibt = Session(1, "296", Corner("T1", 400, 10, 0.3, early: 4)) with
        {
            Id = "ibt",
            StartedUtc = live.StartedUtc.AddMinutes(1),
            Laps = [new LapRow { Number = 1, Time = 130, Clean = true }, new LapRow { Number = 2, Time = 131, Clean = true }],
        };
        // Same laps and best lap, but the start times don't overlap: still the same drive.
        var liveLater = Session(2, "296", Corner("T1", 400, 10, 0.3)) with { Id = "live2", Source = "live" };
        var ibtLater = liveLater with { Id = "ibt2", Source = "ibt", StartedUtc = liveLater.StartedUtc.AddMinutes(5) };
        var otherDay = Session(3, "296", Corner("T1", 400, 10, 0.3));

        var h = TrackHistory.Build([live, ibt, liveLater, ibtLater, otherDay], "suzuka", "296")!;

        Assert.Equal(["ibt", "ibt2", "s3-296"], h.Sessions.Select(s => s.ReportId));
    }

    [Fact]
    public void TheLiveDebriefPutsTheFocusCornerFirst()
    {
        var track = TrackSimulator.Standard();
        var normal = track.SpeedProfile();
        var slow = track.SpeedProfile(new Dictionary<int, double> { [1] = 0.95, [3] = 0.85 });

        LapDebrief Last(IReadOnlyList<int> focus)
        {
            var debriefs = new List<LapDebrief>();
            var session = new CoachingSession(new CoachingOptions { CornerMapLaps = 3 });
            var coach = PostLapCoach.Attach(session, debriefs.Add);
            coach.FocusApexM = focus;
            foreach (var f in track.Drive([normal, normal, normal, normal, slow]))
            {
                session.Observe(f);
            }

            session.Flush();
            return debriefs[^1];
        }

        var plain = Last([]);
        Assert.True(plain.Corners.Count >= 2);
        var lesser = plain.Corners[^1];
        Assert.False(lesser.IsFocus);

        var focused = Last([lesser.Corner.ApexM]);
        Assert.Equal(lesser.Corner.Name, focused.Corners[0].Corner.Name);
        Assert.True(focused.Corners[0].IsFocus);
    }
}
