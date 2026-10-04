using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class FuelCellOrderTests
{
    [Fact]
    public void Defaults_KeepTheOrderAndGroupingTheWidgetAlwaysHad()
    {
        var options = new FuelCalculatorOptions();

        Assert.Equal([FuelCell.LastLap, FuelCell.Average, FuelCell.Minimum, FuelCell.Maximum], options.UsageOrder);
        Assert.Equal([FuelCell.FuelRemaining, FuelCell.FuelToFinish, FuelCell.Refuel], options.StrategyOrder);
        Assert.True(options.UsageOnTop);

        options.Vertical = true;
        Assert.False(options.UsageOnTop);
    }

    [Fact]
    public void AGroupsOrder_KeepsOnlyItsOwnCells_AndAppendsAnyMissing()
    {
        var options = new FuelCalculatorOptions
        {
            UsageOrder = [FuelCell.Maximum, FuelCell.Refuel, FuelCell.Maximum, FuelCell.Average],
        };

        Assert.Equal([FuelCell.Maximum, FuelCell.Average, FuelCell.LastLap, FuelCell.Minimum], options.UsageOrder);
    }

    [Theory]
    [InlineData(FuelGroupOrder.UsageFirst, false, true)]
    [InlineData(FuelGroupOrder.UsageFirst, true, true)]
    [InlineData(FuelGroupOrder.StrategyFirst, false, false)]
    [InlineData(FuelGroupOrder.StrategyFirst, true, false)]
    public void AChosenFirstGroup_HoldsInBothOrientations(FuelGroupOrder first, bool vertical, bool usageOnTop)
    {
        var options = new FuelCalculatorOptions { FirstGroup = first, Vertical = vertical };

        Assert.Equal(usageOnTop, options.UsageOnTop);
    }
}
