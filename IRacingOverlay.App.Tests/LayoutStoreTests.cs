using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Tests;

public sealed class LayoutStoreTests : IDisposable
{
    private static readonly MonitorRef Ultrawide = new(@"\\?\DISPLAY#PHLC347#5&1&0&UID1#{guid}", "PHLC347", "34M2C3500L", 3440, 1440);
    private static readonly MonitorRef Side = new(@"\\?\DISPLAY#ACI24A4#5&1&0&UID2#{guid}", "ACI24A4", "VG248", 1920, 1080);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "oo-layouts-" + Guid.NewGuid().ToString("N"));

    public LayoutStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string FilePath => Path.Combine(_directory, "saved-layouts.json");

    private LayoutStore NewStore() => new(FilePath);

    [Fact]
    public void SaveAndReload_TwoLayoutsWithOpenSnapshot_RoundTripsIdentically()
    {
        var store = NewStore();
        var race = store.Create("Race", Ultrawide, 3440, 1440);
        race.Add(Widget(WidgetCatalog.Standings, 40, 60, ScaleLevel.L, new JsonObject { ["focusSize"] = 9, ["showGap"] = false }));
        race.Add(Widget(WidgetCatalog.Relative, 40, 700, ScaleLevel.M, new JsonObject { ["focusSize"] = 3 }));
        race.Add(Widget(WidgetCatalog.Fuel, 3000, 1200, ScaleLevel.XS, []));
        race.Widgets[2].Visible = false;
        race.Widgets[1].Locked = true;
        race.SnapEnabled = false;
        race.GridSize = 8;
        store.Save(race);

        var oval = store.Create("Oval", Side, 1920, 1080);
        oval.Add(Widget(WidgetCatalog.Flag, 800, 20, ScaleLevel.XL, new JsonObject { ["maxFlags"] = 2 }));
        oval.Add(Widget(WidgetCatalog.Delta, 860, 900, ScaleLevel.S, []));
        oval.Add(Widget(WidgetCatalog.Weather, 1600, 40, ScaleLevel.XXS, new JsonObject { ["compact"] = true }));
        store.Save(oval);

        store.SetOpen(new OpenLayoutState(race.Id,
        [
            new WidgetSnapshot(WidgetCatalog.Standings, true, 120.5, 80, ScaleLevel.M, 0.8, true, new JsonObject { ["focusSize"] = 7 }),
            new WidgetSnapshot(WidgetCatalog.Relative, false, null, null, ScaleLevel.M, 1, false, []),
        ]));

        var reloaded = NewStore();

        Assert.Equal(Serialize(store.List()), Serialize(reloaded.List()));
        Assert.Equal(Serialize(store.Open), Serialize(reloaded.Open));
        Assert.Equal(["Race", "Oval"], reloaded.List().Select(layout => layout.Name));
        Assert.Equal(3, reloaded.Get(race.Id)!.Widgets.Count);
        Assert.Equal(ScaleLevel.L, reloaded.Get(race.Id)!.WidgetOf(WidgetCatalog.Standings)!.Scale);
        Assert.Equal(race.Id, reloaded.Open!.LayoutId);
        Assert.Null(reloaded.Open.Snapshot[1].Left);
    }

    [Fact]
    public void File_HasTheDocumentedShape()
    {
        var store = NewStore();
        store.Create("Race", Ultrawide, 3440, 1440);

        var root = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();

        Assert.Equal(1, (int)root["schemaVersion"]!);
        Assert.Single(root["layouts"]!.AsArray());
        Assert.Null(root["open"]);
        Assert.True(root.ContainsKey("open"));
    }

    [Fact]
    public void Create_SameNameTwice_SuffixesTheSecond()
    {
        var store = NewStore();

        store.Create("Carrera", Ultrawide, 3440, 1440);
        var second = store.Create("Carrera", Ultrawide, 3440, 1440);

        Assert.Equal("Carrera (2)", second.Name);
    }

    [Fact]
    public void Rename_ToAnotherLayoutsName_Suffixes_ButKeepingItsOwnNameDoesNot()
    {
        var store = NewStore();
        store.Create("Race", Ultrawide, 3440, 1440);
        var oval = store.Create("Oval", Ultrawide, 3440, 1440);

        Assert.Equal("Oval", store.Rename(oval.Id, "Oval"));
        Assert.Equal("Race (2)", store.Rename(oval.Id, "Race"));
        Assert.Null(store.Rename(Guid.NewGuid(), "Nope"));
    }

    [Fact]
    public void Duplicate_IsANewDeepCopy_AndChangingItLeavesTheOriginalAlone()
    {
        var store = NewStore();
        var race = store.Create("Race", Ultrawide, 3440, 1440);
        race.Add(Widget(WidgetCatalog.Standings, 40, 60, ScaleLevel.M, new JsonObject { ["focusSize"] = 7 }));
        store.Save(race);

        var copy = store.Duplicate(race.Id)!;
        copy.Widgets[0].Config["focusSize"] = 15;
        copy.Widgets[0].X = 999;
        store.Save(copy);

        var original = store.Get(race.Id)!;
        Assert.Equal("Race (2)", copy.Name);
        Assert.NotEqual(race.Id, copy.Id);
        Assert.Equal(7, (int)original.Widgets[0].Config["focusSize"]!);
        Assert.Equal(40, original.Widgets[0].X);
        Assert.Equal(15, (int)store.Get(copy.Id)!.Widgets[0].Config["focusSize"]!);
    }

    [Fact]
    public void Get_ReturnsACopy_SoUnsavedEditsDoNotLeakIntoTheStore()
    {
        var store = NewStore();
        var race = store.Create("Race", Ultrawide, 3440, 1440);

        store.Get(race.Id)!.Add(Widget(WidgetCatalog.Fuel, 0, 0, ScaleLevel.M, []));

        Assert.Empty(store.Get(race.Id)!.Widgets);
    }

    [Fact]
    public void Delete_RemovesTheLayoutFromDisk()
    {
        var store = NewStore();
        var race = store.Create("Race", Ultrawide, 3440, 1440);

        Assert.True(store.Delete(race.Id));
        Assert.False(store.Delete(race.Id));
        Assert.Empty(NewStore().List());
    }

    [Fact]
    public void Load_DropsInvalidWidgetsFromAHandEditedFile()
    {
        var store = NewStore();
        var race = store.Create("Race", Ultrawide, 3440, 1440);
        race.Add(Widget(WidgetCatalog.Relative, 1, 1, ScaleLevel.M, []));
        store.Save(race);

        var root = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        var widgets = root["layouts"]![0]!["widgets"]!.AsArray();
        widgets.Add(widgets[0]!.DeepClone());
        widgets.Add(new JsonObject { ["type"] = "LapTimer" });
        File.WriteAllText(FilePath, root.ToJsonString());

        Assert.Single(NewStore().Get(race.Id)!.Widgets);
    }

    [Fact]
    public void MissingFile_IsAnEmptyStore()
    {
        var store = NewStore();

        Assert.Empty(store.List());
        Assert.Null(store.Open);
        Assert.False(File.Exists(FilePath));
    }

    private static LayoutWidget Widget(string type, double x, double y, ScaleLevel scale, JsonObject config) => new()
    {
        Type = type,
        X = x,
        Y = y,
        Scale = scale,
        Width = 300,
        Height = 200,
        ZIndex = 1,
        Opacity = 0.75,
        HideOutsideCar = true,
        Config = config,
    };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
}
