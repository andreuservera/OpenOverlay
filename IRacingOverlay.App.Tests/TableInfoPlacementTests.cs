using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class TableInfoPlacementTests
{
    [Fact]
    public void Defaults_KeepTheLayoutTheTablesAlwaysHad()
    {
        var options = new DriverTableOptions(DriverTable.Standings);

        Assert.Equal(TableSlot.TopLeft, options.SlotOf(TableInfoElement.SessionType));
        Assert.Equal(TableSlot.TopRight, options.SlotOf(TableInfoElement.Sof));
        Assert.Equal(TableSlot.BottomLeft, options.SlotOf(TableInfoElement.SessionLaps));
        Assert.Equal(TableSlot.BottomRight, options.SlotOf(TableInfoElement.SessionTime));
    }

    [Fact]
    public void ABand_ShowsOnlyWhileSomethingIsInIt()
    {
        var options = new DriverTableOptions(DriverTable.Relative);
        options.SetSlot(TableInfoElement.SessionLaps, TableSlot.TopCenter);
        options.SetSlot(TableInfoElement.SessionTime, TableSlot.TopRight);

        Assert.True(options.ShowTopInfo);
        Assert.False(options.ShowFooter);

        options.SetSlot(TableInfoElement.Sof, TableSlot.BottomCenter);
        Assert.True(options.ShowFooter);

        options.SetShown(TableInfoElement.Sof, false);
        Assert.False(options.ShowFooter);
    }

    [Fact]
    public void Hidden_KeepsTheSlot_SoShowingItAgainPutsItBack()
    {
        var (options, infoBar) = InfoBar();
        var sof = infoBar.Chips.Single(chip => chip.Element == TableInfoElement.Sof);

        infoBar.Move(sof, TableSlot.BottomCenter);
        Assert.Equal(TableSlot.BottomCenter, options.SlotOf(TableInfoElement.Sof));

        infoBar.Move(sof, null);
        Assert.False(options.ShowSof);
        Assert.Equal(TableSlot.BottomCenter, options.SlotOf(TableInfoElement.Sof));
    }

    [Fact]
    public void Diagram_PutsEachChipInItsSlot_AndTheHiddenOnesInTheTray()
    {
        var (options, infoBar) = InfoBar();

        foreach (var chip in infoBar.Chips)
        {
            var inSlots = infoBar.Top.Concat(infoBar.Bottom).Where(slot => slot.Chips.Contains(chip)).ToList();
            if (options.IsShown(chip.Element))
            {
                Assert.Equal(options.SlotOf(chip.Element), Assert.Single(inSlots).Slot);
                Assert.DoesNotContain(chip, infoBar.Available);
            }
            else
            {
                Assert.Empty(inSlots);
                Assert.Contains(chip, infoBar.Available);
            }
        }
    }

    [Fact]
    public void Dropping_MovesTheChip_AndTheDiagramFollows()
    {
        var (options, infoBar) = InfoBar();
        var hidden = infoBar.Available.First();

        infoBar.Move(hidden, TableSlot.TopCenter);

        Assert.True(options.IsShown(hidden.Element));
        Assert.Contains(hidden, infoBar.SlotFor(TableSlot.TopCenter).Chips);
        Assert.DoesNotContain(hidden, infoBar.Available);
        Assert.Equal(3, infoBar.Top.Count);
        Assert.Equal(3, infoBar.Bottom.Count);
    }

    private static (DriverTableOptions Options, InfoBarSetting InfoBar) InfoBar()
    {
        var options = new DriverTableOptions(DriverTable.Standings);
        var context = new WidgetSettingsContext(options, new DriverTableOptions(DriverTable.Relative), new FuelCalculatorOptions(),
            new FlagOptions(), new CockpitOptions(), new WeatherOptions(), new TrackInfoOptions(), new DeltaOptions(), new PedalTraceOptions(), SaveToStores: false);
        var infoBar = WidgetSettings.For(WidgetCatalog.Standings, context).SelectMany(group => group.Items).OfType<InfoBarSetting>().Single();
        return (options, infoBar);
    }
}
