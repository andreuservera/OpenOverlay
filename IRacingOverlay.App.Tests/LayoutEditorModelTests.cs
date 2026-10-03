using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Tests;

public sealed class LayoutEditorModelTests
{
    private static readonly MonitorRef Monitor = new(@"\\?\DISPLAY#PHLC347#5&1&0&UID1#{guid}", "PHLC347", "34M2C3500L", 1920, 1080);

    private static LayoutEditorModel NewModel(params string[] types) => NewModel(null, types);

    private static LayoutEditorModel NewModel(Func<DateTime>? clock, params string[] types)
    {
        var layout = new Layout("Race", Monitor, 1920, 1080);
        foreach (var type in types)
        {
            layout.Add(new LayoutWidget { Type = type, Width = 200, Height = 100 });
        }

        return new LayoutEditorModel(layout, clock);
    }

    [Fact]
    public void NewModel_IsCleanWithNoHistory()
    {
        var model = NewModel(WidgetCatalog.Relative);

        Assert.False(model.IsDirty);
        Assert.False(model.CanUndo);
        Assert.False(model.CanRedo);
    }

    [Fact]
    public void UndoAndRedo_CoverMoveResizeAddRemove()
    {
        var model = NewModel(WidgetCatalog.Relative);

        model.Move(WidgetCatalog.Relative, 100, 50);
        model.Resize(WidgetCatalog.Relative, ScaleLevel.XL, 90, 40);
        model.Add(new LayoutWidget { Type = WidgetCatalog.Fuel });
        model.Remove(WidgetCatalog.Relative);

        Assert.False(model.Layout.Contains(WidgetCatalog.Relative));

        model.Undo(); // remove
        Assert.Equal(ScaleLevel.XL, model.Layout.WidgetOf(WidgetCatalog.Relative)!.Scale);
        model.Undo(); // add
        Assert.False(model.Layout.Contains(WidgetCatalog.Fuel));
        model.Undo(); // resize
        Assert.Equal((ScaleLevel.M, 100.0, 50.0), Placement(model, WidgetCatalog.Relative));
        model.Undo(); // move
        Assert.Equal((ScaleLevel.M, 0.0, 0.0), Placement(model, WidgetCatalog.Relative));
        Assert.False(model.CanUndo);
        Assert.False(model.IsDirty);

        model.Redo();
        model.Redo();
        Assert.Equal((ScaleLevel.XL, 90.0, 40.0), Placement(model, WidgetCatalog.Relative));
        Assert.True(model.IsDirty);
    }

    [Fact]
    public void AGesture_IsOneUndoStep_HoweverManyMovesItTook()
    {
        var model = NewModel(WidgetCatalog.Relative);

        model.BeginGesture();
        for (var x = 1; x <= 30; x++)
        {
            model.PreviewMove(WidgetCatalog.Relative, x, x);
        }

        model.EndGesture();
        model.Undo();

        Assert.Equal(0, model.Layout.WidgetOf(WidgetCatalog.Relative)!.X);
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void AGestureThatChangesNothing_RecordsNothing()
    {
        var model = NewModel(WidgetCatalog.Relative);

        model.BeginGesture();
        model.EndGesture();

        Assert.False(model.CanUndo);
    }

    [Fact]
    public void ANewChange_ForgetsWhatCouldBeRedone()
    {
        var model = NewModel(WidgetCatalog.Relative);
        model.Move(WidgetCatalog.Relative, 10, 10);
        model.Undo();

        model.Move(WidgetCatalog.Relative, 20, 20);

        Assert.False(model.CanRedo);
    }

    [Fact]
    public void History_KeepsAtLeastFiftySteps()
    {
        var model = NewModel(WidgetCatalog.Relative);
        for (var i = 1; i <= 60; i++)
        {
            model.Move(WidgetCatalog.Relative, i, 0);
        }

        for (var i = 0; i < 50; i++)
        {
            model.Undo();
        }

        Assert.Equal(10, model.Layout.WidgetOf(WidgetCatalog.Relative)!.X);
        Assert.True(LayoutEditorModel.HistoryLimit >= 50);
    }

    [Fact]
    public void Add_ARepeatedType_ThrowsAndRecordsNothing()
    {
        var model = NewModel(WidgetCatalog.Relative);

        Assert.Throws<LayoutRuleException>(() => model.Add(new LayoutWidget { Type = WidgetCatalog.Relative }));

        Assert.False(model.CanUndo);
        Assert.Single(model.Layout.Widgets);
    }

    [Fact]
    public void Add_PutsTheNewWidgetOnTop()
    {
        var model = NewModel(WidgetCatalog.Relative, WidgetCatalog.Standings);

        model.Add(new LayoutWidget { Type = WidgetCatalog.Fuel });

        Assert.Equal(model.Layout.Widgets.Max(widget => widget.ZIndex), model.Layout.WidgetOf(WidgetCatalog.Fuel)!.ZIndex);
    }

    [Fact]
    public void BringToFrontAndSendToBack_ReorderTheStack()
    {
        var model = NewModel(WidgetCatalog.Relative, WidgetCatalog.Standings, WidgetCatalog.Fuel);

        model.BringToFront(WidgetCatalog.Relative);
        Assert.Equal([WidgetCatalog.Standings, WidgetCatalog.Fuel, WidgetCatalog.Relative], Stack(model));

        model.SendToBack(WidgetCatalog.Fuel);
        Assert.Equal([WidgetCatalog.Fuel, WidgetCatalog.Standings, WidgetCatalog.Relative], Stack(model));
    }

    [Fact]
    public void ALockedWidget_IgnoresMovesAndResizes_ButCanBeHidden()
    {
        var model = NewModel(WidgetCatalog.Relative);
        model.SetLocked(WidgetCatalog.Relative, true);

        model.Move(WidgetCatalog.Relative, 300, 300);
        model.Resize(WidgetCatalog.Relative, ScaleLevel.XXL, 0, 0);
        model.BeginGesture();
        model.PreviewMove(WidgetCatalog.Relative, 500, 500);
        model.EndGesture();
        model.SetVisible(WidgetCatalog.Relative, false);

        var widget = model.Layout.WidgetOf(WidgetCatalog.Relative)!;
        Assert.Equal((0.0, 0.0, ScaleLevel.M), (widget.X, widget.Y, widget.Scale));
        Assert.False(widget.Visible);
    }

    [Fact]
    public void ChangeTarget_ScalePositions_KeepsThePlaceOnScreen()
    {
        var model = NewModel(WidgetCatalog.Relative);
        model.Move(WidgetCatalog.Relative, 960, 540);

        model.ChangeTarget(Monitor with { Width = 3840, Height = 2160 }, 3840, 2160, scalePositions: true);

        Assert.Equal((1920.0, 1080.0), XY(model));
        Assert.Equal((3840, 2160), (model.Layout.Width, model.Layout.Height));
        Assert.Equal(ScaleLevel.M, model.Layout.WidgetOf(WidgetCatalog.Relative)!.Scale);
    }

    [Fact]
    public void ChangeTarget_KeepPixels_LeavesPositionsAlone()
    {
        var model = NewModel(WidgetCatalog.Relative);
        model.Move(WidgetCatalog.Relative, 960, 540);

        model.ChangeTarget(Monitor, 2560, 1440, scalePositions: false);

        Assert.Equal((960.0, 540.0), XY(model));
    }

    [Fact]
    public void ChangeTarget_OutOfRange_ThrowsAndRecordsNothing()
    {
        var model = NewModel(WidgetCatalog.Relative);

        Assert.Throws<LayoutRuleException>(() => model.ChangeTarget(Monitor, 7681, 1080, scalePositions: true));

        Assert.False(model.CanUndo);
        Assert.Equal(1920, model.Layout.Width);
    }

    [Fact]
    public void MeasuredSize_IsNeitherAChangeNorAStep()
    {
        var model = NewModel(WidgetCatalog.Relative);

        model.SetMeasuredSize(WidgetCatalog.Relative, 345, 410);

        Assert.False(model.IsDirty);
        Assert.False(model.CanUndo);
        Assert.Equal(345, model.Layout.WidgetOf(WidgetCatalog.Relative)!.Width);
    }

    [Fact]
    public void MarkSaved_MakesTheCurrentStateClean_AndKeepsHistory()
    {
        var model = NewModel(WidgetCatalog.Relative);
        model.Move(WidgetCatalog.Relative, 10, 10);

        var saved = model.Layout.Clone();
        saved.Name = "Race (2)";
        model.MarkSaved(saved);

        Assert.False(model.IsDirty);
        Assert.Equal("Race (2)", model.Layout.Name);
        Assert.True(model.CanUndo);
    }

    [Fact]
    public void AdoptName_SurvivesUndoAndDoesNotDirty()
    {
        var model = NewModel(WidgetCatalog.Relative);
        model.Move(WidgetCatalog.Relative, 10, 10);
        model.Undo();

        model.AdoptName("Endurance");
        model.Redo();
        model.Undo();

        Assert.Equal("Endurance", model.Layout.Name);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void SetSnap_IsAnUndoableChange()
    {
        var model = NewModel();

        model.SetSnap(false, 25);
        Assert.Equal((false, 25), (model.Layout.SnapEnabled, model.Layout.GridSize));

        model.Undo();
        Assert.Equal((true, 10), (model.Layout.SnapEnabled, model.Layout.GridSize));
    }

    [Fact]
    public void ConfigOpacityAndAutoHide_AreUndoableSteps()
    {
        var model = NewModel(WidgetCatalog.Relative);

        model.SetConfig(WidgetCatalog.Relative, new System.Text.Json.Nodes.JsonObject { ["focusSize"] = 9 });
        model.SetHideOutsideCar(WidgetCatalog.Relative, true);
        Assert.True(model.IsDirty);

        model.Undo();
        Assert.False(model.Layout.WidgetOf(WidgetCatalog.Relative)!.HideOutsideCar);
        model.Undo();
        Assert.Empty(model.Layout.WidgetOf(WidgetCatalog.Relative)!.Config);
        Assert.False(model.IsDirty);

        model.Redo();
        Assert.Equal(9, (int)model.Layout.WidgetOf(WidgetCatalog.Relative)!.Config["focusSize"]!);
    }

    [Fact]
    public void ARunOfChangesToOneSetting_IsOneStep_WithinTheMergeWindow()
    {
        var now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        var model = NewModel(() => now, WidgetCatalog.Relative);

        foreach (var opacity in new[] { 0.9, 0.8, 0.7, 0.6 })
        {
            model.SetOpacity(WidgetCatalog.Relative, opacity);
            now += TimeSpan.FromMilliseconds(100);
        }

        model.Undo();

        Assert.Equal(1, model.Layout.WidgetOf(WidgetCatalog.Relative)!.Opacity);
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void ChangesFurtherApartThanTheMergeWindow_OrToAnotherSetting_AreSeparateSteps()
    {
        var now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        var model = NewModel(() => now, WidgetCatalog.Relative);

        model.SetOpacity(WidgetCatalog.Relative, 0.9);
        now += LayoutEditorModel.MergeWindow + TimeSpan.FromMilliseconds(1);
        model.SetOpacity(WidgetCatalog.Relative, 0.8);
        model.SetConfig(WidgetCatalog.Relative, new System.Text.Json.Nodes.JsonObject { ["focusSize"] = 9 });

        model.Undo();
        Assert.Equal(0.8, model.Layout.WidgetOf(WidgetCatalog.Relative)!.Opacity);
        model.Undo();
        Assert.Equal(0.9, model.Layout.WidgetOf(WidgetCatalog.Relative)!.Opacity);
        model.Undo();
        Assert.Equal(1, model.Layout.WidgetOf(WidgetCatalog.Relative)!.Opacity);
    }

    [Fact]
    public void AnUndo_EndsTheRun_SoTheNextChangeIsItsOwnStep()
    {
        var now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        var model = NewModel(() => now, WidgetCatalog.Relative, WidgetCatalog.Fuel);
        model.SetOpacity(WidgetCatalog.Relative, 0.9);
        model.Move(WidgetCatalog.Fuel, 5, 5);
        model.Undo();

        model.SetOpacity(WidgetCatalog.Relative, 0.5);
        model.Undo();

        Assert.Equal(0.9, model.Layout.WidgetOf(WidgetCatalog.Relative)!.Opacity);
    }

    private static (ScaleLevel, double, double) Placement(LayoutEditorModel model, string type)
    {
        var widget = model.Layout.WidgetOf(type)!;
        return (widget.Scale, widget.X, widget.Y);
    }

    private static (double, double) XY(LayoutEditorModel model)
    {
        var widget = model.Layout.WidgetOf(WidgetCatalog.Relative)!;
        return (widget.X, widget.Y);
    }

    private static string[] Stack(LayoutEditorModel model) =>
        model.Layout.Widgets.OrderBy(widget => widget.ZIndex).Select(widget => widget.Type).ToArray();
}
