using System.Text.Json.Nodes;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

/// <summary>
/// The settings rows a widget's page and the layout editor share. Every test runs them against
/// fresh options with saving to the stores off, the way the editor does — so none of this touches
/// the user's settings files.
/// </summary>
public sealed class WidgetSettingsTests
{
    private int _changes;

    public static TheoryData<string> CatalogTypes()
    {
        var data = new TheoryData<string>();
        foreach (var descriptor in WidgetCatalog.All)
        {
            data.Add(descriptor.Key);
        }

        return data;
    }

    [Theory]
    [InlineData(WidgetCatalog.Standings, new[] { "COLUMNS", "TABLE" })]
    [InlineData(WidgetCatalog.Relative, new[] { "COLUMNS", "TABLE" })]
    [InlineData(WidgetCatalog.FuelCalculator, new[] { "BLOCKS", "DISPLAY", "CALCULATION" })]
    [InlineData(WidgetCatalog.Flag, new[] { "FLAG TYPES", "CONTENT", "LAYOUT" })]
    [InlineData(WidgetCatalog.Cockpit, new[] { "MODULES", "UPDATE RATE" })]
    [InlineData(WidgetCatalog.PedalTrace, new[] { "ELEMENTS", "UPDATE RATE" })]
    [InlineData(WidgetCatalog.Weather, new[] { "ELEMENTS", "DISPLAY" })]
    [InlineData(WidgetCatalog.TrackInfo, new[] { "ELEMENTS" })]
    [InlineData(WidgetCatalog.Delta, new[] { "REFERENCE" })]
    [InlineData(WidgetCatalog.TireInfo, new string[0])]
    public void For_ReturnsTheWidgetPagesOwnGroups_InPageOrder(string type, string[] titles)
    {
        var (context, _) = NewContext();

        Assert.Equal(titles, WidgetSettings.For(type, context).Select(group => group.Title));
    }

    /// <summary>
    /// Each row, changed on its own, must reach the options and show up in what the widget's codec
    /// reads back — otherwise an option offered in the editor would silently not be kept in the
    /// layout. It must also report the change exactly once, which is how the editor records it.
    /// </summary>
    [Theory]
    [MemberData(nameof(CatalogTypes))]
    public void EveryRow_ChangesTheConfigTheLayoutKeeps_AndReportsItOnce(string type)
    {
        var (context, codec) = NewContext(type);

        foreach (var row in Rows(WidgetSettings.For(type, context)))
        {
            var before = codec!.Read();
            var changesBefore = _changes;

            row.Change();

            Assert.False(JsonNode.DeepEquals(before, codec.Read()), $"{type} › {row.Name} did not change the layout config");
            Assert.Equal(changesBefore + 1, _changes);
        }
    }

    [Fact]
    public void HeaderToggles_DoNotNudgeAnyLiveWidget_WhenThereIsNone()
    {
        var (context, _) = NewContext(WidgetCatalog.Relative);
        var placement = WidgetSettings.For(WidgetCatalog.Relative, context)
            .SelectMany(group => group.Items).OfType<ChoiceSetting>()
            .First(setting => setting.Label == "SOF");

        placement.SelectedIndex = 0; // Hidden

        Assert.False(context.Relative.ShowSof);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void HeaderToggles_NudgeTheLiveWidget_WhenAskedTo_LikeThePageAlwaysHas()
    {
        var nudged = new List<DriverTable>();
        var (context, _) = NewContext();
        context = context with { TableHeaderChanged = nudged.Add };
        var placement = WidgetSettings.For(WidgetCatalog.Standings, context)
            .SelectMany(group => group.Items).OfType<ChoiceSetting>()
            .First(setting => setting.Label == "Session type");

        placement.SelectedIndex = 0; // Hidden

        Assert.Equal([DriverTable.Standings], nudged);
    }

    private (WidgetSettingsContext Context, IWidgetConfigCodec? Codec) NewContext(string? type = null)
    {
        var targets = new WidgetConfigTargets(
            new DriverTableOptions(DriverTable.Standings),
            new DriverTableOptions(DriverTable.Relative),
            new FlagOptions(),
            new CockpitOptions(),
            new WeatherOptions(),
            new TrackInfoOptions(),
            new FuelCalculatorOptions(),
            new DeltaOptions(),
            new PedalTraceOptions(),
            new WidgetConfigPersistence(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }),
            _ => { });
        var context = new WidgetSettingsContext(
            targets.Standings,
            targets.Relative,
            targets.FuelCalculator,
            targets.Flag,
            targets.Cockpit,
            targets.Weather,
            targets.TrackInfo,
            targets.Delta,
            targets.PedalTrace,
            SaveToStores: false,
            Changed: () => _changes++);
        return (context, type is null ? null : WidgetConfigCodecs.Create(targets)[type]);
    }

    private sealed record Row(string Name, Action Change);

    /// <summary>Every row as something that changes its value to a different valid one.</summary>
    private static IEnumerable<Row> Rows(IEnumerable<SettingsGroup> groups)
    {
        foreach (var setting in groups.SelectMany(group => group.Items))
        {
            switch (setting)
            {
                case ToggleSetting toggle:
                    yield return new Row(toggle.Label, () => toggle.Value = !toggle.Value);
                    break;
                case ChoiceSetting choice:
                    yield return new Row(choice.Label, () => choice.SelectedIndex = (choice.SelectedIndex + 1) % choice.Options.Count);
                    break;
                case SegmentedSetting segmented:
                    yield return new Row(segmented.Label, () => segmented.SelectedIndex = (segmented.SelectedIndex + 1) % segmented.Options.Count);
                    break;
                case NumberSetting number:
                    yield return new Row(number.Label, () => (number.Value + number.Step <= number.Maximum
                        ? number.IncrementCommand
                        : number.DecrementCommand).Execute(null));
                    break;
                case SliderSetting slider:
                    yield return new Row(slider.Label, () => slider.Value += slider.Value + slider.Step <= slider.Maximum ? slider.Step : -slider.Step);
                    break;
                case ReorderListSetting columns:
                    yield return new Row($"{columns.Label} › order", () => columns.Move(0, columns.Items.Count - 1));
                    foreach (var chip in columns.Items.SelectMany(column => new[] { column.Visible, column.Companion }).OfType<ChipSetting>())
                    {
                        yield return new Row($"{columns.Label} › {chip.Label}", () => chip.Value = !chip.Value);
                    }

                    break;
                case ChipGroupSetting chips:
                    foreach (var chip in chips.Chips)
                    {
                        yield return new Row($"{chips.Label} › {chip.Label}", () => chip.Value = !chip.Value);
                    }

                    break;
                default:
                    throw new InvalidOperationException($"No way to exercise {setting.GetType().Name} \"{setting.Label}\"");
            }
        }
    }
}
