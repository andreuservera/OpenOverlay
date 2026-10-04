using System.IO;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Tests;

/// <summary>Folding settings groups and rail sections. CollapseStore is static, so these run against
/// a temporary file and never the real settings folder.</summary>
[Collection("Collapse store")]
public sealed class CollapsibleSectionTests : IDisposable
{
    private readonly string _originalPath = CollapseStore.FilePath;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oo-collapse-" + Guid.NewGuid().ToString("N"));

    public CollapsibleSectionTests()
    {
        Directory.CreateDirectory(_dir);
        CollapseStore.FilePath = Path.Combine(_dir, "collapsed-sections.json");
        CollapseStore.Reset();
    }

    public void Dispose()
    {
        CollapseStore.FilePath = _originalPath;
        CollapseStore.Reset();
        Directory.Delete(_dir, recursive: true);
    }

    private static SettingsGroup Group(string scope, string title = "COLUMNS") =>
        new SettingsGroup(title) { Scope = scope }.With(new InfoSetting("Row", null, "x"));

    [Fact]
    public void Group_StartsOpen_AndStaysFolded_WhenThePageIsRebuilt()
    {
        var first = Group("Standings");
        Assert.False(first.IsCollapsed);

        first.IsCollapsed = true;

        Assert.True(Group("Standings").IsCollapsed);
        Assert.False(Group("Relative").IsCollapsed);            // same title, other widget
        Assert.False(Group("Standings", "TABLE").IsCollapsed);  // other group, same widget
    }

    [Fact]
    public void FoldedState_SurvivesARestart()
    {
        Group("Standings").IsCollapsed = true;
        CollapseStore.Reset();

        Assert.True(Group("Standings").IsCollapsed);

        Group("Standings").IsCollapsed = false;
        CollapseStore.Reset();
        Assert.False(Group("Standings").IsCollapsed);
    }

    [Fact]
    public void SearchResults_ShowOpen_WithoutForgettingTheFold()
    {
        Group("Standings").IsCollapsed = true;

        var found = Group("Standings");
        found.KeepOpen = true;

        Assert.False(found.IsCollapsed);
        Assert.True(Group("Standings").IsCollapsed);
    }

    [Fact]
    public void AGroupWithNoRows_CannotFold()
    {
        var notice = new SettingsGroup("CONTROLLED BY LAYOUT", "A notice.") { Scope = "Standings" };

        notice.IsCollapsed = true;

        Assert.False(notice.IsCollapsible);
        Assert.False(notice.IsCollapsed);
    }

    [Fact]
    public void RailSection_FoldsAndRemembers()
    {
        var section = NavSection.For("APPLICATION");
        Assert.Same(section, NavSection.For("APPLICATION"));
        section.IsCollapsed = false;

        section.IsCollapsed = true;
        CollapseStore.Reset();

        Assert.True(NavSection.For("APPLICATION").IsCollapsed);
        Assert.False(NavSection.For("WIDGETS").IsCollapsed);
    }
}
