using IRacingOverlay.App.About;

namespace IRacingOverlay.App.Tests;

public sealed class ChangelogTests
{
    // The shape CHANGELOG.md is written in: a title, "###" versions, "####" sections, nested bullets
    // and a rule between versions.
    private const string Sample = """
        ## Changelog

        All notable changes to OpenOverlay, newest first.

        ### [Unreleased]

        #### Added
        - Not released yet.

        ### [0.7.0] - 2026-09-30

        #### Added
        - New Weather widget.
        - New cockpit theme system:
          - Casual
          - Hypercar

        #### Reliability
        - Crash recovery mechanisms.

        ---

        ### [0.1.0] - 2026-07-22

        The first public release.

        #### Added
        - Floating widgets.
        """;

    [Fact]
    public void ReadsEveryVersion_NewestFirst_AndIgnoresTheTitleAndUnreleased()
    {
        var changelog = Changelog.Parse(Sample);

        Assert.Equal([new ReleaseVersion(0, 7, 0), new ReleaseVersion(0, 1, 0)], changelog.Entries.Select(entry => entry.Version));
        Assert.Same(changelog.Entries[0], changelog.Current);
        Assert.Equal(new DateOnly(2026, 9, 30), changelog.Entries[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 22), changelog.Entries[1].Date);
    }

    [Fact]
    public void KeepsTheSectionsAsWritten_WithTheirNestedBullets()
    {
        var entry = Changelog.Parse(Sample).Entries[0];

        Assert.Equal(["Added", "Reliability"], entry.Sections.Select(section => section.Title));
        Assert.Equal(
            [new ChangelogItem("New Weather widget.", []), new ChangelogItem("New cockpit theme system:", ["Casual", "Hypercar"])],
            entry.Sections[0].Items,
            ItemComparer.Instance);
        Assert.Equal(3, entry.ItemCount);
        Assert.Null(entry.Summary);
    }

    [Fact]
    public void TextBeforeTheFirstSection_IsTheSummary()
    {
        var entry = Changelog.Parse(Sample).Entries[1];

        Assert.Equal("The first public release.", entry.Summary);
        Assert.Equal(["Floating widgets."], entry.Sections.Single().Items.Select(item => item.Text));
    }

    [Fact]
    public void ReadsKeepAChangelogStyle_WithoutItsLinkDefinitionsOrComments()
    {
        var changelog = Changelog.Parse("""
            # Changelog
            <!-- Add new entries at the top. -->

            ## [Unreleased]

            ## [v1.2.0-beta.1] - 2026-10-01
            ### Fixed
            - Relative gaps. <!-- was #42 -->

            [Unreleased]: https://github.com/andreuservera/OpenOverlay/compare/v1.2.0-beta.1...HEAD
            [v1.2.0-beta.1]: https://github.com/andreuservera/OpenOverlay/releases/tag/v1.2.0-beta.1
            """);

        var entry = Assert.Single(changelog.Entries);
        Assert.Equal(new ReleaseVersion(1, 2, 0, "beta.1"), entry.Version);
        Assert.Equal("Fixed", entry.Sections.Single().Title);
        Assert.Equal(["Relative gaps."], entry.Sections.Single().Items.Select(item => item.Text));
    }

    [Fact]
    public void AnotherHeadingAtTheVersionsLevel_EndsTheEntry()
    {
        var entry = Changelog.Parse("""
            ### [1.0.0]
            #### Added
            - Kept.
            ### Contributors
            - Not a change.
            """).Entries.Single();

        Assert.Equal(["Kept."], entry.Sections.Single().Items.Select(item => item.Text));
        Assert.Null(entry.Date);
    }

    [Fact]
    public void WrappedLinesContinueTheirBullet_AndParagraphsAreLinesOfTheirOwn()
    {
        var items = Changelog.Parse("""
            ### [1.0.0]
            #### Notes
            - A bullet
              that wraps.
              - A nested bullet
                that wraps too.

            A paragraph
            on two lines.
            """).Entries.Single().Sections.Single().Items;

        Assert.Equal(
            [new ChangelogItem("A bullet that wraps.", ["A nested bullet that wraps too."]), new ChangelogItem("A paragraph on two lines.", [])],
            items,
            ItemComparer.Instance);
    }

    [Fact]
    public void InlineMarkdown_IsReducedToWhatAReaderSees()
    {
        var item = Changelog.Parse("""
            ### [1.0.0]
            - **Bold**, _emphasis_, `code`, a [link](https://example.com), <https://example.com> and \[brackets\].
            """).Entries.Single().Sections.Single().Items.Single();

        Assert.Equal("Bold, emphasis, code, a link, https://example.com and [brackets].", item.Text);
    }

    [Fact]
    public void BulletsOutsideASection_GoUnderAnUntitledOne_AndEmptySectionsAreDropped()
    {
        var entry = Changelog.Parse("""
            ### [1.0.0]
            - Loose.
            #### Fixed
            """).Entries.Single();

        var section = Assert.Single(entry.Sections);
        Assert.Equal("", section.Title);
        Assert.Equal(["Loose."], section.Items.Select(item => item.Text));
    }

    [Theory]
    [InlineData("Breaking Changes", true)]
    [InlineData("BREAKING", true)]
    [InlineData("Fixed", false)]
    public void ABreakingSection_IsAWarning(string title, bool expected)
    {
        Assert.Equal(expected, new ChangelogSection(title, []).IsWarning);
    }

    [Fact]
    public void WindowsLineEndings_ReadTheSame()
    {
        var unix = Changelog.Parse(Sample);
        var windows = Changelog.Parse(Sample.ReplaceLineEndings("\r\n"));

        Assert.Equal(unix.Entries.Select(entry => (entry.Version, entry.Date, entry.Summary, entry.ItemCount)),
            windows.Entries.Select(entry => (entry.Version, entry.Date, entry.Summary, entry.ItemCount)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Just some text.")]
    [InlineData("# Changelog\n\n## [Unreleased]\n- Soon.")]
    [InlineData("### [1.2] - 2026-01-01\n- Not a version.")]
    [InlineData("###[1.2.3]\n- Not a heading.")]
    [InlineData("\0\u00FF### ]][[ ### ####### ---")]
    public void WithoutAVersionHeading_ThereAreNoEntries(string markdown)
    {
        var changelog = Changelog.Parse(markdown);

        Assert.Empty(changelog.Entries);
        Assert.Null(changelog.Current);
    }

    private sealed class ItemComparer : IEqualityComparer<ChangelogItem>
    {
        public static readonly ItemComparer Instance = new();

        public bool Equals(ChangelogItem? x, ChangelogItem? y) =>
            x is not null && y is not null && x.Text == y.Text && x.Children.SequenceEqual(y.Children);

        public int GetHashCode(ChangelogItem item) => item.Text.GetHashCode(StringComparison.Ordinal);
    }
}
