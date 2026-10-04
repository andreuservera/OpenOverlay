using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class TrackInfoOptionsTests
{
    [Fact]
    public void SeparatorsOnlySitBetweenGroupsThatAreBothShowing()
    {
        var options = new TrackInfoOptions();
        Assert.True(options.ShowFirstSeparator);
        Assert.True(options.ShowSecondSeparator);

        // Every condition hidden: the conditions group goes, and so does the separator after it.
        options.ShowAirTemp = false;
        options.ShowTrackTemp = false;
        options.ShowWind = false;
        options.ShowHumidity = false;
        Assert.True(options.ShowConditions);
        options.ShowTrackUsage = false;
        Assert.False(options.ShowConditions);
        Assert.False(options.ShowSecondSeparator);
        Assert.True(options.ShowFirstSeparator);

        options.ShowTimeLeft = false;
        options.ShowLap = false;
        Assert.False(options.ShowTiming);
        Assert.False(options.ShowFirstSeparator);
    }
}
