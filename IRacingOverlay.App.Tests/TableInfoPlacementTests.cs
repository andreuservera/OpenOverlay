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
        var options = new DriverTableOptions(DriverTable.Standings);
        var context = new WidgetSettingsContext(options, new DriverTableOptions(DriverTable.Relative), new FuelCalculatorOptions(),
            new FlagOptions(), new CockpitOptions(), new WeatherOptions(), new TrackInfoOptions(), new DeltaOptions(), new PedalTraceOptions(), SaveToStores: false);
        var sof = WidgetSettings.For(WidgetCatalog.Standings, context)
            .SelectMany(group => group.Items).OfType<ChoiceSetting>().First(setting => setting.Label == "SOF");

        sof.SelectedIndex = 5; // Bottom center
        Assert.Equal(TableSlot.BottomCenter, options.SlotOf(TableInfoElement.Sof));

        sof.SelectedIndex = 0; // Hidden
        Assert.False(options.ShowSof);
        Assert.Equal(TableSlot.BottomCenter, options.SlotOf(TableInfoElement.Sof));
    }
}
