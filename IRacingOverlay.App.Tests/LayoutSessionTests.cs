using System.IO;
using System.Text.Json.Nodes;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Tests;

public sealed class LayoutSessionTests : IDisposable
{
    private static readonly DisplayMonitor Ultrawide = new("u", "PHLC347", "34M2C3500L", @"\\.\DISPLAY1", 0, 0, 3440, 1440, IsPrimary: true);
    private static readonly DisplayMonitor Side = new("s", "ACI24A4", "VG248", @"\\.\DISPLAY2", 3440, 357, 1920, 1080, IsPrimary: false);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "oo-session-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHost _host = new();
    private readonly LayoutStore _store;
    private readonly LayoutSession _session;

    public LayoutSessionTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new LayoutStore(Path.Combine(_directory, "saved-layouts.json"));
        _session = new LayoutSession(_store, _host);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Open_AppliesEveryVisibleWidget_AtItsPositionOnTheMonitor_AndLeavesTheRestAlone()
    {
        var layout = Layout(Side,
            Widget(WidgetCatalog.Relative, 100, 200),
            Widget(WidgetCatalog.Standings, 50, 60, visible: false));

        _session.Open(layout, new LayoutPlacement(Side, ScalePositions: false, PixelsPerDip: 1));

        Assert.Equal((3540.0, 557.0), _host.State[WidgetCatalog.Relative].Position);
        Assert.True(_host.State[WidgetCatalog.Relative].Enabled);
        Assert.Equal(_host.Original(WidgetCatalog.Standings), _host.State[WidgetCatalog.Standings]);
        Assert.Equal([WidgetCatalog.Relative], _store.Open!.Snapshot.Select(entry => entry.Type));
    }

    [Fact]
    public void Close_PutsEveryWidgetBackExactlyAsItWas()
    {
        var layout = Layout(Side, Widget(WidgetCatalog.Relative, 100, 200), Widget(WidgetCatalog.Fuel, 10, 10));
        _session.Open(layout, new LayoutPlacement(Side, false, 1));

        _session.Close();

        Assert.Equal(_host.Original(WidgetCatalog.Relative), _host.State[WidgetCatalog.Relative]);
        Assert.Equal(_host.Original(WidgetCatalog.Fuel), _host.State[WidgetCatalog.Fuel]);
        Assert.Null(_store.Open);
    }

    [Fact]
    public void Close_UndoesChangesMadeWhileOpen_TheyAreTemporary()
    {
        _session.Open(Layout(Side, Widget(WidgetCatalog.Relative, 100, 200)), new LayoutPlacement(Side, false, 1));
        _host.State[WidgetCatalog.Relative] = _host.State[WidgetCatalog.Relative] with { Position = (1, 1), Scale = ScaleLevel.XXL };

        _session.Close();

        Assert.Equal(_host.Original(WidgetCatalog.Relative), _host.State[WidgetCatalog.Relative]);
    }

    [Fact]
    public void SwitchingLayouts_MovesSharedWidgetsStraightAcross_AndCloseStillRestoresTheOriginals()
    {
        var race = Layout(Side, Widget(WidgetCatalog.Relative, 100, 200), Widget(WidgetCatalog.Standings, 10, 10));
        var oval = Layout(Side, Widget(WidgetCatalog.Relative, 700, 20), Widget(WidgetCatalog.Fuel, 50, 50));
        _session.Open(race, new LayoutPlacement(Side, false, 1));
        _host.Calls.Clear();

        _session.Open(oval, new LayoutPlacement(Side, false, 1));

        // The shared Relative is never put back in between, so it doesn't jump on screen.
        Assert.DoesNotContain($"Restore {WidgetCatalog.Relative}", _host.Calls);
        Assert.DoesNotContain($"Capture {WidgetCatalog.Relative}", _host.Calls);
        Assert.Equal((4140.0, 377.0), _host.State[WidgetCatalog.Relative].Position);
        Assert.Equal(_host.Original(WidgetCatalog.Standings), _host.State[WidgetCatalog.Standings]);
        Assert.Contains($"Capture {WidgetCatalog.Fuel}", _host.Calls);
        Assert.Equal(oval.Id, _store.Open!.LayoutId);

        _session.Close();

        Assert.Equal(_host.Original(WidgetCatalog.Relative), _host.State[WidgetCatalog.Relative]);
        Assert.Equal(_host.Original(WidgetCatalog.Fuel), _host.State[WidgetCatalog.Fuel]);
        Assert.Equal(_host.Original(WidgetCatalog.Standings), _host.State[WidgetCatalog.Standings]);
    }

    [Fact]
    public void OpeningAnotherLayout_ReleasesTheFirst_SoOnlyItsWidgetsAreControlled()
    {
        var race = Layout(Side, Widget(WidgetCatalog.Relative, 100, 200));
        var oval = Layout(Side, Widget(WidgetCatalog.Fuel, 10, 10));
        _session.Open(race, new LayoutPlacement(Side, false, 1));

        _session.Open(oval, new LayoutPlacement(Side, false, 1));

        Assert.Equal(_host.Original(WidgetCatalog.Relative), _host.State[WidgetCatalog.Relative]);
        Assert.Equal(oval.Id, _store.Open!.LayoutId);

        _session.Close();
        Assert.Equal(_host.Original(WidgetCatalog.Fuel), _host.State[WidgetCatalog.Fuel]);
    }

    [Fact]
    public void ReopeningTheSameLayout_KeepsTheOriginalCapture()
    {
        var layout = Layout(Side, Widget(WidgetCatalog.Relative, 100, 200));
        _session.Open(layout, new LayoutPlacement(Side, false, 1));

        _session.Open(layout, new LayoutPlacement(Side, false, 1));
        _session.Close();

        Assert.Equal(_host.Original(WidgetCatalog.Relative), _host.State[WidgetCatalog.Relative]);
    }

    [Fact]
    public void Reapply_RestoresWidgetsNoLongerControlled_CapturesNewOnes_AndKeepsTheOriginals()
    {
        var layout = Layout(Side, Widget(WidgetCatalog.Relative, 100, 200), Widget(WidgetCatalog.Fuel, 10, 10));
        _session.Open(layout, new LayoutPlacement(Side, false, 1));

        layout.Remove(WidgetCatalog.Fuel);
        layout.WidgetOf(WidgetCatalog.Relative)!.X = 300;
        layout.Add(Widget(WidgetCatalog.Delta, 5, 5));
        _session.Reapply(layout, new LayoutPlacement(Side, false, 1));

        Assert.Equal(_host.Original(WidgetCatalog.Fuel), _host.State[WidgetCatalog.Fuel]);
        Assert.Equal(3740, _host.State[WidgetCatalog.Relative].Position!.Value.Left);
        Assert.True(_host.State[WidgetCatalog.Delta].Enabled);

        _session.Close();
        Assert.Equal(_host.Original(WidgetCatalog.Relative), _host.State[WidgetCatalog.Relative]);
        Assert.Equal(_host.Original(WidgetCatalog.Delta), _host.State[WidgetCatalog.Delta]);
    }

    [Fact]
    public void TheCapture_SurvivesARestart_SoCloseStillRestores()
    {
        _session.Open(Layout(Side, Widget(WidgetCatalog.Relative, 100, 200)), new LayoutPlacement(Side, false, 1));

        var afterRestart = new LayoutSession(new LayoutStore(Path.Combine(_directory, "saved-layouts.json")), _host);
        afterRestart.Close();

        Assert.Equal(_host.Original(WidgetCatalog.Relative), _host.State[WidgetCatalog.Relative]);
    }

    [Fact]
    public void Placement_ScalesPositionsToTheMonitor_WhenAskedTo()
    {
        var layout = new Layout("Race", Side.ToRef(), 1920, 1080);
        var placement = new LayoutPlacement(Ultrawide, ScalePositions: true, PixelsPerDip: 1);

        Assert.Equal((3440.0, 1440.0), placement.ToDesktop(layout, 1920, 1080));
        Assert.Equal((1720.0, 720.0), placement.ToDesktop(layout, 960, 540));
    }

    [Fact]
    public void Placement_KeepsPixels_WhenNotScaling_AndConvertsToWpfUnits()
    {
        var layout = new Layout("Race", Side.ToRef(), 1920, 1080);
        var placement = new LayoutPlacement(Side, ScalePositions: false, PixelsPerDip: 1.5);

        Assert.Equal((2360.0, 304.67), placement.ToDesktop(layout, 100, 100));
    }

    [Fact]
    public void Open_RemembersTheScaleChoice_ForReapplying()
    {
        _session.Open(Layout(Side, Widget(WidgetCatalog.Relative, 0, 0)), new LayoutPlacement(Ultrawide, ScalePositions: true, 1));

        Assert.True(_store.Open!.ScalePositions);
    }

    private Layout Layout(DisplayMonitor monitor, params LayoutWidget[] widgets)
    {
        var layout = _store.Create("Layout", monitor.ToRef(), monitor.Width, monitor.Height);
        foreach (var widget in widgets)
        {
            layout.Add(widget);
        }

        return _store.Save(layout);
    }

    private static LayoutWidget Widget(string type, double x, double y, bool visible = true) => new()
    {
        Type = type,
        X = x,
        Y = y,
        Scale = ScaleLevel.L,
        Opacity = 0.5,
        HideOutsideCar = true,
        Visible = visible,
        Config = new JsonObject { ["fromLayout"] = true },
    };

    private sealed record WidgetState(bool Enabled, (double Left, double Top)? Position, ScaleLevel Scale, double Opacity, bool Hide, string Config);

    /// <summary>Widgets as plain records: every type starts switched off, never placed, at M, with
    /// its own config, so a test can compare against how it started.</summary>
    private sealed class FakeHost : ILayoutWidgetHost
    {
        public FakeHost()
        {
            foreach (var descriptor in WidgetCatalog.All)
            {
                State[descriptor.Key] = Original(descriptor.Key);
            }
        }

        public Dictionary<string, WidgetState> State { get; } = [];

        public List<string> Calls { get; } = [];

        public WidgetState Original(string type) => new(false, type == WidgetCatalog.Fuel ? (7, 8) : null, ScaleLevel.M, 1, false, $"{{\"own\":\"{type}\"}}");

        public WidgetSnapshot Capture(string type)
        {
            Calls.Add($"Capture {type}");
            var state = State[type];
            return new WidgetSnapshot(type, state.Enabled, state.Position?.Left, state.Position?.Top, state.Scale, state.Opacity, state.Hide,
                JsonNode.Parse(state.Config)!.AsObject());
        }

        public void Apply(LayoutWidget widget, double left, double top)
        {
            Calls.Add($"Apply {widget.Type}");
            State[widget.Type] = new WidgetState(true, (left, top), widget.Scale, widget.Opacity, widget.HideOutsideCar, widget.Config.ToJsonString());
        }

        public void Restore(WidgetSnapshot snapshot)
        {
            Calls.Add($"Restore {snapshot.Type}");
            State[snapshot.Type] = new WidgetState(
                snapshot.Enabled,
                snapshot.Left is { } left && snapshot.Top is { } top ? (left, top) : null,
                snapshot.Scale,
                snapshot.Opacity,
                snapshot.HideOutsideCar,
                snapshot.Config.ToJsonString());
        }
    }
}
