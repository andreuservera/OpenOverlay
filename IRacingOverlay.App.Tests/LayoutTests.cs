using System.Text.Json.Nodes;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Layouts;

namespace IRacingOverlay.App.Tests;

public sealed class LayoutTests
{
    private static readonly MonitorRef Monitor = new(@"\\?\DISPLAY#PHLC347#5&1&0&UID1#{guid}", "PHLC347", "34M2C3500L", 1920, 1080);

    private static Layout NewLayout() => new("Race", Monitor, 1920, 1080);

    [Fact]
    public void Add_RepeatedType_IsRejectedWithAClearMessage()
    {
        var layout = NewLayout();
        layout.Add(new LayoutWidget { Type = WidgetCatalog.Relative });

        var error = Assert.Throws<LayoutRuleException>(() => layout.Add(new LayoutWidget { Type = WidgetCatalog.Relative }));

        Assert.Contains("already has the Relative widget", error.Message);
        Assert.Single(layout.Widgets);
    }

    [Fact]
    public void ControlledBottomToTop_FollowsTheLayers_SkipsHiddenWidgets_AndKeepsListOrderOnTies()
    {
        var layout = NewLayout();
        layout.Add(new LayoutWidget { Type = WidgetCatalog.Relative, ZIndex = 2 });
        layout.Add(new LayoutWidget { Type = WidgetCatalog.Standings, ZIndex = 0 });
        layout.Add(new LayoutWidget { Type = WidgetCatalog.Incident, ZIndex = 1, Visible = false });
        layout.Add(new LayoutWidget { Type = WidgetCatalog.Delta, ZIndex = 2 });

        Assert.Equal(
            [WidgetCatalog.Standings, WidgetCatalog.Relative, WidgetCatalog.Delta],
            layout.ControlledBottomToTop().Select(widget => widget.Type));
    }

    [Fact]
    public void Add_UnknownType_IsRejected()
    {
        var layout = NewLayout();

        var error = Assert.Throws<LayoutRuleException>(() => layout.Add(new LayoutWidget { Type = "LapTimer" }));

        Assert.Contains("LapTimer", error.Message);
        Assert.Empty(layout.Widgets);
    }

    [Fact]
    public void Add_EveryCatalogType_FillsTheLayoutToItsMaximum()
    {
        var layout = NewLayout();

        foreach (var descriptor in WidgetCatalog.All)
        {
            layout.Add(new LayoutWidget { Type = descriptor.Key });
        }

        Assert.Equal(WidgetCatalog.All.Count, Layout.MaxWidgets);
        Assert.Equal(Layout.MaxWidgets, layout.Widgets.Count);
    }

    [Theory]
    [InlineData(639, 1080)]
    [InlineData(7681, 1080)]
    [InlineData(1920, 639)]
    [InlineData(1920, 7681)]
    public void Resolution_OutOfRange_IsRejected(int width, int height)
    {
        Assert.Throws<LayoutRuleException>(() => new Layout("Race", Monitor, width, height));
        Assert.Throws<LayoutRuleException>(() => NewLayout().SetResolution(width, height));
    }

    [Theory]
    [InlineData(640, 640)]
    [InlineData(7680, 7680)]
    [InlineData(640, 7680)]
    public void Resolution_AtTheLimits_IsAccepted(int width, int height)
    {
        var layout = new Layout("Race", Monitor, width, height);
        layout.SetResolution(width, height);

        Assert.Equal((width, height), (layout.Width, layout.Height));
    }

    [Fact]
    public void Duplicate_IsDeep_SoChangingTheCopyLeavesTheOriginalAlone()
    {
        var original = NewLayout();
        original.Add(new LayoutWidget { Type = WidgetCatalog.Standings, X = 10, Config = new JsonObject { ["focusSize"] = 7 } });

        var copy = original.Duplicate("Race (2)");
        copy.Widgets[0].X = 500;
        copy.Widgets[0].Config["focusSize"] = 12;
        copy.Add(new LayoutWidget { Type = WidgetCatalog.Incident });

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(10, original.Widgets[0].X);
        Assert.Equal(7, (int)original.Widgets[0].Config["focusSize"]!);
        Assert.Single(original.Widgets);
    }

    [Fact]
    public void Normalize_DropsUnknownAndRepeatedTypes_KeepingTheFirst()
    {
        var layout = NewLayout();
        layout.Add(new LayoutWidget { Type = WidgetCatalog.Relative, X = 1 });
        // Simulates a file edited by hand or written by another version.
        var widgets = (List<LayoutWidget>)typeof(Layout)
            .GetField("_widgets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(layout)!;
        widgets.Add(new LayoutWidget { Type = WidgetCatalog.Relative, X = 2 });
        widgets.Add(new LayoutWidget { Type = "LapTimer" });

        var problems = layout.Normalize();

        Assert.Equal(2, problems.Count);
        Assert.Equal(1, Assert.Single(layout.Widgets).X);
    }

    [Theory]
    [InlineData("Race", new string[0], "Race")]
    [InlineData("Race", new[] { "Race" }, "Race (2)")]
    [InlineData("race", new[] { "Race", "Race (2)" }, "race (3)")]
    [InlineData("Race (2)", new[] { "Race", "Race (2)" }, "Race (3)")]
    [InlineData("  ", new string[0], "Layout")]
    public void Unique_SuffixesTakenNames(string desired, string[] taken, string expected)
    {
        Assert.Equal(expected, LayoutNaming.Unique(desired, taken));
    }
}
