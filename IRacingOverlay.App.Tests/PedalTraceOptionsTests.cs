using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class PedalTraceOptionsTests
{
    [Fact]
    public void Default_ShowsEveryBlock_GearFirstAndBarsLast()
    {
        var options = new PedalTraceOptions();

        Assert.Equal(
            [PedalTraceElement.Gear, PedalTraceElement.Speed, PedalTraceElement.Steering, PedalTraceElement.Trace, PedalTraceElement.Pedals],
            options.Strip());
    }

    [Fact]
    public void HiddenBlocks_LeaveTheStrip_AndKeepTheirPlace()
    {
        var options = new PedalTraceOptions { ElementOrder = [PedalTraceElement.Pedals, PedalTraceElement.Trace] };

        options.ShowSpeed = false;

        Assert.Equal(
            [PedalTraceElement.Pedals, PedalTraceElement.Trace, PedalTraceElement.Gear, PedalTraceElement.Steering],
            options.Strip());
        Assert.Contains(PedalTraceElement.Speed, options.ElementOrder);
    }

    [Fact]
    public void Order_DropsRepeatsAndUnknowns_AndAppendsWhatIsMissing()
    {
        var order = PedalTraceOptions.Normalize([PedalTraceElement.Trace, PedalTraceElement.Trace, (PedalTraceElement)99]);

        Assert.Equal(PedalTraceElement.Trace, order[0]);
        Assert.Equal(PedalTraceOptions.DefaultOrder.Count, order.Count);
    }

    [Fact]
    public void Changes_AreAnnounced()
    {
        var options = new PedalTraceOptions();
        var changed = new List<string?>();
        options.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        options.ShowSteering = false;
        options.ElementOrder = [PedalTraceElement.Pedals];

        Assert.Equal([nameof(PedalTraceOptions.ShowSteering), nameof(PedalTraceOptions.ElementOrder)], changed);
    }
}
