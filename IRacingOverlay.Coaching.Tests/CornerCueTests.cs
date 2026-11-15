using IRacingOverlay.Coaching.Advice;
using IRacingOverlay.Coaching.Corners;

namespace IRacingOverlay.Coaching.Tests;

public sealed class CornerCueTests
{
    private static readonly CornerMap Map = new(3000, [new Corner("T1", 500, 600, 700), new Corner("T2", 2900, 2950, 2990)]);

    private static LapDebrief Debrief(FindingKind? kind) => new(1, 100, 0.3, false,
        kind is { } k ? [new CornerAdvice(Map.Corners[0], 0.2, [new CornerFinding(k, 10)], 0)] : []);

    [Fact]
    public void TheCueComesOnceALapAboutThreeSecondsBeforeTheCorner()
    {
        var cues = new CornerCues([new FocusCue(600, "T1", nameof(FindingKind.BrakedTooEarly))]);
        var said = new List<string>();
        cues.Cue += c => said.Add(c.Words);

        // 50 m/s: three seconds is 150 m, so the cue comes from 350 m on.
        cues.Observe(Map, 1, 300, 50, false);
        Assert.Empty(said);
        cues.Observe(Map, 1, 360, 50, false);
        cues.Observe(Map, 1, 400, 50, false);
        Assert.Equal(["brake later"], said);
        cues.Observe(Map, 2, 380, 50, false);
        Assert.Equal(2, said.Count);
        // Not in the pit lane.
        cues.Observe(Map, 3, 380, 50, true);
        Assert.Equal(2, said.Count);
    }

    [Fact]
    public void ACornerJustAfterTheLineIsCuedBeforeTheLine()
    {
        var map = new CornerMap(3000, [new Corner("T1", 60, 150, 250)]);
        var cues = new CornerCues([new FocusCue(150, "T1", nameof(FindingKind.LateThrottle))]);
        var said = 0;
        cues.Cue += _ => said++;

        cues.Observe(map, 4, 2950, 50, false);

        Assert.Equal(1, said);
    }

    [Fact]
    public void TheCueStopsOnceTheMistakeStaysAwayForThreeLaps()
    {
        var cues = new CornerCues([new FocusCue(600, "T1", nameof(FindingKind.BrakedTooEarly))]);
        var fixedCount = 0;
        cues.Fixed += _ => fixedCount++;

        cues.OnDebrief(Debrief(null));
        cues.OnDebrief(Debrief(FindingKind.BrakedTooEarly)); // came back: count again
        cues.OnDebrief(Debrief(FindingKind.LateThrottle));
        cues.OnDebrief(Debrief(null));
        Assert.True(cues.Active);
        cues.OnDebrief(Debrief(null));

        Assert.False(cues.Active);
        Assert.Equal(1, fixedCount);
    }
}
