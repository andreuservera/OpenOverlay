using IRacingOverlay.App.About;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Tests;

public sealed class ReleaseCatalogTests
{
    [Fact]
    public void TheBuildsVersion_IsTheNewestChangelogEntry()
    {
        Assert.NotNull(ReleaseCatalog.Current);
        Assert.Equal(ReleaseCatalog.Current.Version, BuildInfo.Release);
    }

    [Fact]
    public void EveryEntryInTheChangelog_HasADateAndSaysSomething()
    {
        var changelog = ReleaseCatalog.Load(typeof(ReleaseCatalog).Assembly);

        Assert.Contains(changelog.Entries, entry => entry.Version == new ReleaseVersion(0, 1, 0));
        Assert.All(changelog.Entries, entry =>
        {
            Assert.NotNull(entry.Date);
            Assert.True(entry.ItemCount > 0 || entry.Summary is not null, $"{entry.Version} has no changes or summary");
        });
    }

    [Fact]
    public void TheChangelogIsNewestFirst()
    {
        var versions = ReleaseCatalog.Changelog.Entries.Select(entry => entry.Version).ToList();

        Assert.Equal(versions.OrderDescending(), versions);
        Assert.Equal(versions.Count, versions.Distinct().Count());
    }

    [Fact]
    public void AnAssemblyWithoutAChangelog_GivesAnEmptyOne()
    {
        Assert.Same(Changelog.Empty, ReleaseCatalog.Load(typeof(ReleaseCatalogTests).Assembly));
    }
}
