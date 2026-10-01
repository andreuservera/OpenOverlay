using IRacingOverlay.App.About;

namespace IRacingOverlay.App.Tests;

public sealed class VersionTrackerTests
{
    private static readonly DateTime Monday = new(2026, 9, 28, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tuesday = Monday.AddDays(1);

    private static VersionHistory Ran(params string[] versions) => new(
        versions.Select((version, day) => new InstalledVersion(version, Monday.AddDays(-versions.Length + day))).ToList(),
        versions.LastOrDefault(),
        null);

    [Fact]
    public void AFreshInstall_IsAFirstRun_WithNothingToAnnounce()
    {
        var (history, change) = VersionTracker.Observe(null, "0.5.0", hasEarlierRuns: false, Monday);

        Assert.Equal(new VersionChange(VersionChangeKind.FirstRun, "0.5.0", null), change);
        Assert.Null(history.PendingNotice);
        Assert.Equal([new InstalledVersion("0.5.0", Monday)], history.Versions);
        Assert.Equal("0.5.0", history.HighestRelease);
    }

    [Fact]
    public void AnUpdateToANewRelease_IsAnnounced()
    {
        var (history, change) = VersionTracker.Observe(Ran("0.4.0"), "0.5.0", hasEarlierRuns: true, Tuesday);

        Assert.Equal(new VersionChange(VersionChangeKind.Updated, "0.5.0", "0.4.0"), change);
        Assert.Equal("0.5.0", history.PendingNotice);
        Assert.Equal("0.5.0", history.HighestRelease);
        Assert.Equal(new InstalledVersion("0.5.0", Tuesday), history.Versions[^1]);
    }

    [Fact]
    public void AnUpdateFromAVersionThatPredatesTracking_IsAnnounced()
    {
        var (history, change) = VersionTracker.Observe(null, "0.5.0", hasEarlierRuns: true, Monday);

        Assert.Equal(new VersionChange(VersionChangeKind.Updated, "0.5.0", null), change);
        Assert.Equal("0.5.0", history.PendingNotice);
    }

    [Fact]
    public void TheNoticeStaysOwedUntilShown_ThenNeverReturns()
    {
        var (updated, _) = VersionTracker.Observe(Ran("0.4.0"), "0.5.0", true, Monday);

        var (stillOwed, sameVersion) = VersionTracker.Observe(updated, "0.5.0", true, Tuesday);
        Assert.Equal(VersionChangeKind.Unchanged, sameVersion.Kind);
        Assert.Equal("0.5.0", stillOwed.PendingNotice);

        var shown = VersionTracker.NoticeShown(stillOwed);
        var (afterRestart, _) = VersionTracker.Observe(shown, "0.5.0", true, Tuesday.AddDays(1));
        Assert.Null(afterRestart.PendingNotice);
        Assert.Equal(Monday, afterRestart.Versions[^1].FirstRunUtc);
    }

    [Fact]
    public void GoingBackAndForward_DoesNotAnnounceTheSameReleaseTwice()
    {
        var (afterDowngrade, downgrade) = VersionTracker.Observe(Ran("0.4.0", "0.5.0"), "0.4.0", true, Monday);
        Assert.Equal(VersionChangeKind.Downgraded, downgrade.Kind);
        Assert.Null(afterDowngrade.PendingNotice);

        var (afterReturn, back) = VersionTracker.Observe(afterDowngrade, "0.5.0", true, Tuesday);
        Assert.Equal(VersionChangeKind.Updated, back.Kind);
        Assert.Null(afterReturn.PendingNotice);

        // One entry per version: the return replaces 0.5.0's first run instead of adding a second.
        Assert.Equal(["0.4.0", "0.5.0"], afterReturn.Versions.Select(v => v.Version));
        Assert.Equal(Tuesday, afterReturn.Versions[^1].FirstRunUtc);
    }

    [Fact]
    public void AnUpdateFromAPreview_ToItsRelease_IsAnnounced()
    {
        var (history, _) = VersionTracker.Observe(Ran("0.4.0", "0.5.0-beta.1"), "0.5.0", true, Tuesday);

        Assert.Equal("0.5.0", history.PendingNotice);
    }

    [Fact]
    public void RebuildingTheSameRelease_IsNotAnUpdate()
    {
        var (_, change) = VersionTracker.Observe(Ran("0.5.0"), "0.5.0+8ad1c11", true, Tuesday);

        Assert.Equal(VersionChangeKind.Unchanged, change.Kind);
    }

    [Fact]
    public void TheHistoryStaysBounded()
    {
        var history = Ran(Enumerable.Range(0, VersionTracker.Remembered).Select(i => $"0.{i}.0").ToArray());

        var (updated, _) = VersionTracker.Observe(history, "1.0.0", true, Tuesday);

        Assert.Equal(VersionTracker.Remembered, updated.Versions.Count);
        Assert.Equal("0.1.0", updated.Versions[0].Version);
        Assert.Equal("1.0.0", updated.Versions[^1].Version);
    }
}
