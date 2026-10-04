using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;
using Json.Schema;

namespace IRacingOverlay.App.Tests;

public sealed class LayoutFileTests : IDisposable
{
    private static readonly MonitorRef Ultrawide = new(@"\\?\DISPLAY#PHLC347#5&1&0&UID1#{guid}", "PHLC347", "34M2C3500L", 3440, 1440);
    private static readonly DateTime ExportedAt = new(2026, 10, 3, 18, 0, 0, 456, DateTimeKind.Utc);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "oo-layout-file-" + Guid.NewGuid().ToString("N"));

    public LayoutFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // ===== Export =====

    [Fact]
    public void Export_WritesTheDocumentedFormat()
    {
        var root = JsonNode.Parse(Export(SampleLayout()))!.AsObject();

        Assert.Equal(1, (int)root["formatVersion"]!);
        Assert.Equal("Race GT3", (string)root["metadata"]!["name"]!);
        Assert.Equal("2026-10-03T18:00:00Z", (string)root["metadata"]!["exportedAt"]!);
        Assert.Equal("0.8.0", (string)root["metadata"]!["appVersion"]!);
        Assert.Equal(Ultrawide.DevicePath, (string)root["monitor"]!["id"]!);
        Assert.Equal("PHLC347", (string)root["monitor"]!["edidId"]!);
        Assert.Equal("34M2C3500L", (string)root["monitor"]!["name"]!);
        Assert.Equal(3440, (int)root["monitor"]!["width"]!);
        Assert.Equal(1440, (int)root["resolution"]!["height"]!);

        var relative = root["widgets"]!.AsArray()[0]!.AsObject();
        Assert.Equal(
            ["type", "x", "y", "scale", "width", "height", "zIndex", "visible", "locked", "opacity", "hideOutsideCar", "config"],
            relative.Select(pair => pair.Key));
        Assert.Equal("Relative", (string)relative["type"]!);
        Assert.Equal(40, (double)relative["x"]!);
        Assert.Equal("L", (string)relative["scale"]!);
        Assert.Equal(0.9, (double)relative["opacity"]!);
        Assert.True((bool)relative["hideOutsideCar"]!);
        Assert.Equal(4, (int)relative["config"]!["focusSize"]!);
    }

    [Fact]
    public void ExportThenImport_GivesAnEquivalentLayout()
    {
        var original = SampleLayout();

        var read = LayoutFile.Import(Export(original), Codecs(), "fallback");

        Assert.Null(read.Error);
        Assert.Empty(read.Warnings);
        var imported = read.Layout!;
        Assert.NotEqual(original.Id, imported.Id);
        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(original.Monitor, imported.Monitor);
        Assert.Equal((original.Width, original.Height), (imported.Width, imported.Height));
        Assert.Equal(original.Widgets.Count, imported.Widgets.Count);
        foreach (var (expected, actual) in original.Widgets.Zip(imported.Widgets))
        {
            Assert.Equal(
                (expected.Type, expected.X, expected.Y, expected.Scale, expected.Width, expected.Height, expected.ZIndex),
                (actual.Type, actual.X, actual.Y, actual.Scale, actual.Width, actual.Height, actual.ZIndex));
            Assert.Equal(
                (expected.Visible, expected.Locked, expected.Opacity, expected.HideOutsideCar),
                (actual.Visible, actual.Locked, actual.Opacity, actual.HideOutsideCar));
            Assert.True(JsonNode.DeepEquals(expected.Config, actual.Config), actual.Type);
            Assert.False(actual.RequiresConfiguration);
        }
    }

    [Fact]
    public void ExportThenImport_EmptyLayout_StaysEmpty()
    {
        var read = LayoutFile.Import(Export(new Layout("Blank", Ultrawide, 1920, 1080)), Codecs(), "fallback");

        Assert.Null(read.Error);
        Assert.Empty(read.Layout!.Widgets);
    }

    [Fact]
    public void Export_LeavesOutSensitiveFields()
    {
        var layout = new Layout("Race", Ultrawide, 1920, 1080);
        layout.Add(new LayoutWidget
        {
            Type = WidgetCatalog.Relative,
            Config = new JsonObject { ["apiToken"] = "s3cr3t", ["password"] = "hunter2", ["focusSize"] = 4 },
        });

        var json = LayoutFile.Export(layout, CodecsWithSensitive(WidgetCatalog.Relative, "apiToken", "password"), "0.8.0", ExportedAt);

        Assert.DoesNotContain("apiToken", json);
        Assert.DoesNotContain("s3cr3t", json);
        Assert.DoesNotContain("password", json);
        Assert.DoesNotContain("hunter2", json);
        Assert.Contains("focusSize", json);
        // Exporting doesn't touch the layout itself.
        Assert.Equal("s3cr3t", (string)layout.Widgets[0].Config["apiToken"]!);
    }

    [Fact]
    public void Export_ConformsToTheSchema_WithEveryWidgetType()
    {
        var codecs = Codecs();
        var layout = new Layout("Everything", Ultrawide, 3440, 1440);
        var z = 0;
        foreach (var descriptor in WidgetCatalog.All)
        {
            layout.Add(new LayoutWidget
            {
                Type = descriptor.Key,
                X = -20 + z * 100.5,
                Y = z * 50,
                Scale = (ScaleLevel)(z % 8),
                Width = 300,
                Height = 120.25,
                ZIndex = z++,
                Visible = z % 2 == 0,
                Locked = z % 3 == 0,
                Opacity = 0.75,
                Config = codecs[descriptor.Key].Read(),
            });
        }

        var results = Schema().Evaluate(JsonDocument.Parse(Export(layout)).RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(results.IsValid, Describe(results));
    }

    [Fact]
    public void Schema_RejectsWhatTheAppRejects()
    {
        var schema = Schema();
        var valid = JsonNode.Parse(Export(SampleLayout()))!.AsObject();
        Assert.True(schema.Evaluate(ToElement(valid)).IsValid);

        var noResolution = (JsonObject)valid.DeepClone();
        noResolution.Remove("resolution");
        var badScale = (JsonObject)valid.DeepClone();
        badScale["widgets"]![0]!["scale"] = "HUGE";
        var newerFormat = (JsonObject)valid.DeepClone();
        newerFormat["formatVersion"] = 2;

        Assert.False(schema.Evaluate(ToElement(noResolution)).IsValid);
        Assert.False(schema.Evaluate(ToElement(badScale)).IsValid);
        Assert.False(schema.Evaluate(ToElement(newerFormat)).IsValid);
    }

    // ===== Import: sensitive fields =====

    [Fact]
    public void Import_SensitiveFieldsInTheFile_AreLeftEmptyAndTheWidgetNeedsConfiguring()
    {
        var json = File(Widget("Relative", config: new JsonObject { ["apiToken"] = "s3cr3t", ["focusSize"] = 4 }), Widget("Incident"));

        var read = LayoutFile.Import(json, CodecsWithSensitive(WidgetCatalog.Relative, "apiToken"), "fallback");

        var relative = read.Layout!.WidgetOf(WidgetCatalog.Relative)!;
        Assert.False(relative.Config.ContainsKey("apiToken"));
        Assert.Equal(4, (int)relative.Config["focusSize"]!);
        Assert.True(relative.RequiresConfiguration);
        Assert.False(read.Layout.WidgetOf(WidgetCatalog.Incident)!.RequiresConfiguration);
    }

    // ===== Import: what is skipped with a warning =====

    [Fact]
    public void Import_UnknownTypes_AreLeftOutWithAWarning()
    {
        var json = File(Widget("LapTimer"), Widget("Relative"), Widget("LapTimer"), Widget("Holograms"));

        var read = LayoutFile.Import(json, Codecs(), "fallback");

        Assert.Null(read.Error);
        Assert.Equal([WidgetCatalog.Relative], read.Layout!.Widgets.Select(widget => widget.Type));
        var warning = Assert.Single(read.Warnings);
        Assert.Contains("3 widgets", warning);
        Assert.Contains("LapTimer, Holograms", warning);
    }

    [Fact]
    public void Import_RepeatedType_KeepsTheFirstWithAWarning()
    {
        var json = File(Widget("Relative", x: 10), Widget("Incident"), Widget("Relative", x: 999));

        var read = LayoutFile.Import(json, Codecs(), "fallback");

        Assert.Null(read.Error);
        Assert.Equal(2, read.Layout!.Widgets.Count);
        Assert.Equal(10, read.Layout.WidgetOf(WidgetCatalog.Relative)!.X);
        Assert.Contains("Relative widget more than once", Assert.Single(read.Warnings));
    }

    [Fact]
    public void Import_MinimalFile_FillsInTheDefaults()
    {
        const string json = """
            { "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 },
              "widgets": [ { "type": "Incident", "x": 1, "y": 2, "scale": "M" }, { "type": "Delta", "x": 3, "y": 4, "scale": "XXXL" } ] }
            """;

        var read = LayoutFile.Import(json, Codecs(), "My file");

        Assert.Null(read.Error);
        var layout = read.Layout!;
        Assert.Equal("My file", layout.Name);
        Assert.Equal("", layout.Monitor.DevicePath);
        var delta = layout.WidgetOf(WidgetCatalog.Delta)!;
        Assert.Equal((ScaleLevel.XXXL, 1, true, false, false, 1.0), (delta.Scale, delta.ZIndex, delta.Visible, delta.Locked, delta.HideOutsideCar, delta.Opacity));
        Assert.Empty(delta.Config);
    }

    [Fact]
    public void Import_MonitorWithoutEdid_TakesItFromTheDevicePath()
    {
        var json = JsonNode.Parse(File(Widget("Incident")))!.AsObject();
        json["monitor"]!["edidId"] = null;

        var read = LayoutFile.Import(json.ToJsonString(), Codecs(), "fallback");

        Assert.Equal("PHLC347", read.Layout!.Monitor.EdidId);
    }

    [Fact]
    public void Import_TakenName_IsRenamedWhenSaved()
    {
        var store = new LayoutStore(Path.Combine(_directory, "saved-layouts.json"));
        store.Create("Race GT3", Ultrawide, 3440, 1440);

        var saved = store.Save(LayoutFile.Import(Export(SampleLayout()), Codecs(), "fallback").Layout!);

        Assert.Equal("Race GT3 (2)", saved.Name);
        Assert.Equal(2, store.List().Count);
    }

    // ===== Import: what refuses the file =====

    public static TheoryData<string, string> InvalidFiles() => new()
    {
        { "{ not json", "isn't valid JSON" },
        { "[1, 2]", "isn't a layout file" },
        { "{}", "isn't a layout file" },
        { """{ "formatVersion": 2, "resolution": { "width": 1920, "height": 1080 }, "widgets": [] }""", "newer version" },
        { """{ "formatVersion": 0, "resolution": { "width": 1920, "height": 1080 }, "widgets": [] }""", "Format 0" },
        { """{ "formatVersion": "1", "resolution": { "width": 1920, "height": 1080 }, "widgets": [] }""", "format version" },
        { """{ "formatVersion": 1, "widgets": [] }""", "resolution" },
        { """{ "formatVersion": 1, "resolution": { "width": 100, "height": 1080 }, "widgets": [] }""", "out of range" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920.5, "height": 1080 }, "widgets": [] }""", "whole number" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 } }""", "widgets" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ 5 ] }""", "widget 1" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ { "x": 1 } ] }""", "type of widget 1" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ { "type": "Incident", "x": "1", "y": 2, "scale": "M" } ] }""", "position of the Incidents widget" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ { "type": "Incident", "y": 2, "scale": "M" } ] }""", "position of the Incidents widget" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ { "type": "Incident", "x": 1, "y": 2, "scale": "HUGE" } ] }""", "isn't one of" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ { "type": "Incident", "x": 1, "y": 2, "scale": "3" } ] }""", "isn't one of" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ { "type": "Incident", "x": 1, "y": 2, "scale": "M", "opacity": 2 } ] }""", "between 0 and 1" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ { "type": "Incident", "x": 1, "y": 2, "scale": "M", "width": -5 } ] }""", "negative" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ { "type": "Incident", "x": 1, "y": 2, "scale": "M", "visible": "yes" } ] }""", "true or false" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ { "type": "Incident", "x": 1, "y": 2, "scale": "M", "config": [] } ] }""", "settings of the Incidents widget" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "monitor": "PHL", "widgets": [] }""", "monitor" },
        { """{ "formatVersion": 1, "resolution": { "width": 1920, "height": 1080 }, "widgets": [ { "type": "LapTimer" } ] }""", "None of the file's widgets" },
    };

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public void Import_InvalidFile_SaysWhyAndGivesNoLayout(string json, string expected)
    {
        var read = LayoutFile.Import(json, Codecs(), "fallback");

        Assert.Null(read.Layout);
        Assert.Contains(expected, read.Error);
    }

    [Fact]
    public void Import_MoreEntriesThanWidgetTypes_IsRefused()
    {
        var entries = WidgetCatalog.All.Select(descriptor => Widget(descriptor.Key)).Append(Widget("LapTimer")).ToArray();

        var read = LayoutFile.Import(File(entries), Codecs(), "fallback");

        Assert.Null(read.Layout);
        Assert.Contains($"at most {Layout.MaxWidgets}", read.Error);
    }

    [Fact]
    public void Import_OneOfEachWidgetType_IsAccepted()
    {
        var read = LayoutFile.Import(File(WidgetCatalog.All.Select(descriptor => Widget(descriptor.Key)).ToArray()), Codecs(), "fallback");

        Assert.Equal(Layout.MaxWidgets, read.Layout!.Widgets.Count);
    }

    // ===== File names =====

    [Theory]
    [InlineData("Race GT3", "Race GT3.layout.json")]
    [InlineData("Race: GT3?", "Race_ GT3_.layout.json")]
    [InlineData("  ", "Layout.layout.json")]
    public void FileNameFor_IsSafeForWindows(string name, string expected)
    {
        Assert.Equal(expected, LayoutFile.FileNameFor(name));
    }

    [Theory]
    [InlineData(@"C:\Layouts\Race GT3.layout.json", "Race GT3")]
    [InlineData(@"C:\Layouts\Race.json", "Race")]
    [InlineData(@"C:\Layouts\.layout.json", "Layout")]
    public void NameFromFileName_DropsTheExtension(string path, string expected)
    {
        Assert.Equal(expected, LayoutFile.NameFromFileName(path));
    }

    // ===== Helpers =====

    private static string Export(Layout layout) => LayoutFile.Export(layout, Codecs(), "0.8.0", ExportedAt);

    private static Layout SampleLayout()
    {
        var codecs = Codecs();
        var layout = new Layout("Race GT3", Ultrawide, 3440, 1440);

        var relative = codecs[WidgetCatalog.Relative].Read();
        relative["focusSize"] = 4;
        layout.Add(new LayoutWidget
        {
            Type = WidgetCatalog.Relative, X = 40, Y = 600, Scale = ScaleLevel.L, Width = 345.5, Height = 410,
            ZIndex = 1, Opacity = 0.9, HideOutsideCar = true, Config = relative,
        });

        layout.Add(new LayoutWidget
        {
            Type = WidgetCatalog.Standings, X = 3000, Y = -10, Scale = ScaleLevel.XS, ZIndex = 0,
            Visible = false, Locked = true, Config = codecs[WidgetCatalog.Standings].Read(),
        });

        var delta = codecs[WidgetCatalog.Delta].Read();
        delta["reference"] = nameof(DeltaReference.OptimalLap);
        layout.Add(new LayoutWidget { Type = WidgetCatalog.Delta, X = 1720.5, Y = 1300, ZIndex = 2, Config = delta });
        return layout;
    }

    private static string File(params JsonObject[] widgets) => new JsonObject
    {
        ["formatVersion"] = 1,
        ["metadata"] = new JsonObject { ["name"] = "Hand made", ["exportedAt"] = "2026-10-03T18:00:00Z", ["appVersion"] = "0.8.0" },
        ["monitor"] = new JsonObject
        {
            ["id"] = Ultrawide.DevicePath, ["edidId"] = Ultrawide.EdidId, ["name"] = Ultrawide.FriendlyName,
            ["width"] = Ultrawide.Width, ["height"] = Ultrawide.Height,
        },
        ["resolution"] = new JsonObject { ["width"] = 3440, ["height"] = 1440 },
        ["widgets"] = new JsonArray(widgets.Cast<JsonNode>().ToArray()),
    }.ToJsonString();

    private static JsonObject Widget(string type, double x = 0, JsonObject? config = null) => new()
    {
        ["type"] = type,
        ["x"] = x,
        ["y"] = 0,
        ["scale"] = "M",
        ["config"] = config ?? [],
    };

    private static IReadOnlyDictionary<string, IWidgetConfigCodec> Codecs() =>
        WidgetConfigCodecs.Create(new WidgetConfigTargets(
            new DriverTableOptions(DriverTable.Standings),
            new DriverTableOptions(DriverTable.Relative),
            new FlagOptions(),
            new CockpitOptions(),
            new WeatherOptions(),
            new FuelCalculatorOptions(),
            new DeltaOptions(),
            new WidgetConfigPersistence(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }),
            _ => { }));

    /// <summary>Today no widget has sensitive fields, so the filter is checked with a stand-in that does.</summary>
    private static IReadOnlyDictionary<string, IWidgetConfigCodec> CodecsWithSensitive(string type, params string[] fields) =>
        new Dictionary<string, IWidgetConfigCodec>(Codecs()) { [type] = new SensitiveCodec(type, fields) };

    // Loaded once: JsonSchema.Net registers a schema by its $id process-wide and refuses a second load.
    private static readonly Lazy<JsonSchema> LayoutSchema = new(LoadSchema);

    private static JsonSchema Schema() => LayoutSchema.Value;

    private static JsonSchema LoadSchema()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "docs", "specs", "layout.schema.json");
            if (System.IO.File.Exists(path))
            {
                return JsonSchema.FromText(System.IO.File.ReadAllText(path));
            }
        }

        throw new FileNotFoundException("docs/specs/layout.schema.json not found above the test output folder");
    }

    private static JsonElement ToElement(JsonNode node) => JsonDocument.Parse(node.ToJsonString()).RootElement;

    private static string Describe(EvaluationResults results) =>
        string.Join(Environment.NewLine, (results.Details ?? []).Where(detail => detail.Errors is not null)
            .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}")));

    private sealed class SensitiveCodec(string type, string[] fields) : IWidgetConfigCodec
    {
        public string Type { get; } = type;

        public IReadOnlySet<string> SensitiveFields { get; } = new HashSet<string>(fields);

        public JsonObject Read() => [];

        public void Apply(JsonObject config)
        {
        }
    }
}
