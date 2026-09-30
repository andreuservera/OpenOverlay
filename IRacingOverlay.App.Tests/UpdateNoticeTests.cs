using IRacingOverlay.App.About;

namespace IRacingOverlay.App.Tests;

public sealed class UpdateNoticeTests
{
    private static ChangelogEntry Entry(params (string Title, string[] Items)[] sections) => new(
        new ReleaseVersion(0, 5, 0),
        new DateOnly(2026, 10, 1),
        null,
        sections.Select(section => new ChangelogSection(section.Title, section.Items.Select(text => new ChangelogItem(text, [])).ToList())).ToList());

    [Fact]
    public void LeadsWithTheFirstSection_AndCountsTheRest()
    {
        var notice = UpdateNotice.For("0.5.0", Entry(
            ("Added", ["One", "Two", "Three", "Four", "Five"]),
            ("Fixed", ["Fix", "Another fix"])));

        Assert.Equal("OpenOverlay has been updated to v0.5.0", notice.Title);
        Assert.Equal("Released 1 Oct 2026. Here's what's new.", notice.Subtitle);
        Assert.Equal("ADDED", notice.Heading);
        Assert.Equal(["One", "Two", "Three", "Four"], notice.Items);
        Assert.Equal("…and 3 more changes.", notice.More);
        Assert.False(notice.HasBreakingChanges);
    }

    [Fact]
    public void AShortEntry_HasNothingMore()
    {
        var notice = UpdateNotice.For("0.5.1", Entry(("Fixed", ["Relative refresh"])));

        Assert.Equal("FIXED", notice.Heading);
        Assert.Equal(["Relative refresh"], notice.Items);
        Assert.Null(notice.More);
    }

    [Fact]
    public void BreakingChanges_AreFlagged_ButNeverTheTeaser()
    {
        var mixed = UpdateNotice.For("1.0.0", Entry(("Breaking Changes", ["Old layouts reset"]), ("Improved", ["Faster"])));
        Assert.Equal("IMPROVED", mixed.Heading);
        Assert.True(mixed.HasBreakingChanges);
        Assert.Equal("…and 1 more change.", mixed.More);

        var onlyBreaking = UpdateNotice.For("1.0.0", Entry(("Breaking changes", ["The TC bar is gone"])));
        Assert.Equal("BREAKING CHANGES", onlyBreaking.Heading);
    }

    [Fact]
    public void BulletsWithoutASection_AreHeadedChanges()
    {
        Assert.Equal("CHANGES", UpdateNotice.For("0.5.0", Entry(("", ["Something"]))).Heading);
    }

    [Fact]
    public void WithoutNotes_StillSaysWhatVersionThisIs()
    {
        var notice = UpdateNotice.For("0.5.0", null);

        Assert.Equal("OpenOverlay has been updated to v0.5.0", notice.Title);
        Assert.Null(notice.Heading);
        Assert.Empty(notice.Items);

        var summaryOnly = UpdateNotice.For("0.5.0", Entry());
        Assert.Equal("Released 1 Oct 2026. The details are in What's New.", summaryOnly.Subtitle);
        Assert.Empty(summaryOnly.Items);
    }
}
