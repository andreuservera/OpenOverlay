using System.Text.Json.Nodes;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class DriverTableColumnOrderTests
{
    [Fact]
    public void DefaultOrder_PutsTheMakeAndFlagBetweenTheNumberAndTheDriver_HiddenSoNothingMoves()
    {
        var layout = new DriverTableOptions(DriverTable.Standings).Columns;

        Assert.Equal(0, layout.Position);
        Assert.Equal(1, layout.CarNumber);
        Assert.Equal(2, layout.CarBrand);
        Assert.Equal(0, layout.Width2);
        Assert.Equal(3, layout.CountryFlag);
        Assert.Equal(0, layout.Width3);
        Assert.Equal(4, layout.Driver);
        Assert.Equal(7, layout.IRating);
        Assert.Equal(8, layout.SlotOf(DriverTableColumn.IRatingDelta));
        Assert.Equal(11, layout.BestLap);
        Assert.Equal(12, layout.LastLap);
        Assert.Equal(13, layout.Gap);
        Assert.Equal(146, layout.Width4);
    }

    [Fact]
    public void Reordering_MovesTheColumnAndItsWidth_AndTheDeltaStaysBesideTheRating()
    {
        var options = new DriverTableOptions(DriverTable.Relative)
        {
            ColumnOrder = [DriverTableColumn.Gap, DriverTableColumn.IRating, DriverTableColumn.Driver],
        };
        var layout = options.Columns;

        Assert.Equal(0, layout.Gap);
        Assert.Equal(58, layout.Width0);
        Assert.Equal(1, layout.IRating);
        Assert.Equal(2, layout.SlotOf(DriverTableColumn.IRatingDelta));
        Assert.Equal(3, layout.Driver);
        // The rest follow in their default order.
        Assert.Equal(4, layout.Position);
        Assert.Equal(DriverTableColumnLayout.DefaultOrder.Count, options.ColumnOrder.Count);
    }

    [Fact]
    public void AHiddenColumn_KeepsItsSlotButTakesNoWidth()
    {
        var options = new DriverTableOptions(DriverTable.Standings);
        options.SetVisible(DriverTableColumn.Driver, false);

        Assert.Equal(4, options.Columns.Driver);
        Assert.Equal(0, options.Columns.Width4);
    }

    [Fact]
    public void Normalize_DropsTheDeltaAndRepeats_AndAppendsWhatIsMissing()
    {
        var order = DriverTableColumnLayout.Normalize(
            [DriverTableColumn.Lap, DriverTableColumn.IRatingDelta, DriverTableColumn.Lap, (DriverTableColumn)99]);

        Assert.Equal(DriverTableColumn.Lap, order[0]);
        Assert.DoesNotContain(DriverTableColumn.IRatingDelta, order);
        Assert.Equal(DriverTableColumnLayout.DefaultOrder.Count, order.Count);
        Assert.Equal(DriverTableColumnLayout.DefaultOrder.Where(c => c != DriverTableColumn.Lap), order.Skip(1));
    }

    [Fact]
    public void Layouts_CarryTheOrder_AndAPartialOrUnknownOneStillApplies()
    {
        var options = new DriverTableOptions(DriverTable.Standings);
        var codec = Codec(options);
        var config = codec.Read();
        Assert.Equal("Position", (string)config["columnOrder"]![0]!);

        config["columnOrder"] = new JsonArray("gap", "LapTimer", "Driver");
        codec.Apply(config);

        Assert.Equal(DriverTableColumn.Gap, options.ColumnOrder[0]);
        Assert.Equal(DriverTableColumn.Driver, options.ColumnOrder[1]);
        Assert.Equal(DriverTableColumnLayout.DefaultOrder.Count, options.ColumnOrder.Count);
    }

    [Fact]
    public void ALayoutWithoutAnOrder_LeavesTheCurrentOneAlone()
    {
        var options = new DriverTableOptions(DriverTable.Standings) { ColumnOrder = [DriverTableColumn.Gap] };
        var codec = Codec(options);
        var config = codec.Read();
        config.Remove("columnOrder");

        codec.Apply(config);

        Assert.Equal(DriverTableColumn.Gap, options.ColumnOrder[0]);
    }

    [Fact]
    public void Dragging_OnlyMovesTheList_AndReleasingAppliesTheOrderOnce()
    {
        var applied = new List<IReadOnlyList<Enum>>();
        var setting = new ReorderListSetting(
            "Columns",
            null,
            DriverTableColumnLayout.DefaultOrder.Select(column => new ReorderListItem(column, new ChipSetting(column.ToString(), null, true, _ => { }))),
            applied.Add);

        setting.MoveLive(0, 1);
        setting.MoveLive(1, 2);
        Assert.Empty(applied);

        setting.CommitOrder();
        setting.CommitOrder();

        var order = Assert.Single(applied);
        Assert.Equal(DriverTableColumn.Position, (DriverTableColumn)order[2]);
        Assert.Equal(DriverTableColumn.CarNumber, (DriverTableColumn)order[0]);
    }

    [Fact]
    public void ADragThatEndsWhereItStarted_AppliesNothing()
    {
        var applied = 0;
        var setting = new ReorderListSetting(
            "Columns",
            null,
            DriverTableColumnLayout.DefaultOrder.Select(column => new ReorderListItem(column, new ChipSetting(column.ToString(), null, true, _ => { }))),
            _ => applied++);

        setting.MoveLive(0, 3);
        setting.MoveLive(3, 0);
        setting.CommitOrder();

        Assert.Equal(0, applied);
    }

    private static IWidgetConfigCodec Codec(DriverTableOptions options) =>
        WidgetConfigCodecs.Create(new WidgetConfigTargets(
            options.Table == DriverTable.Standings ? options : new DriverTableOptions(DriverTable.Standings),
            options.Table == DriverTable.Relative ? options : new DriverTableOptions(DriverTable.Relative),
            new FlagOptions(),
            new CockpitOptions(),
            new WeatherOptions(),
            new TrackInfoOptions(),
            new FuelCalculatorOptions(),
            new DeltaOptions(),
            new PedalTraceOptions(),
            new WidgetConfigPersistence(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }),
            _ => { }))[options.Table == DriverTable.Standings ? WidgetCatalog.Standings : WidgetCatalog.Relative];

    [Fact]
    public void OnlyStandings_HasTheFastestLapSlot()
    {
        Assert.True(new DriverTableOptions(DriverTable.Standings).ShowFastestLapMark);
        Assert.True(new DriverTableOptions(DriverTable.Standings).FastestLapMarkWidth > 0);
        Assert.False(new DriverTableOptions(DriverTable.Relative).ShowFastestLapMark);
        Assert.Equal(0, new DriverTableOptions(DriverTable.Relative).FastestLapMarkWidth);
    }
}
