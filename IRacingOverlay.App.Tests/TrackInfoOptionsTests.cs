using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class TrackInfoOptionsTests
{
    [Fact]
    public void DefaultBar_SeparatesTheThreeGroups()
    {
        var bar = new TrackInfoOptions().Bar();

        Assert.Equal(TrackInfoOptions.DefaultOrder, bar.Select(entry => entry.Field));
        Assert.Equal(
            [TrackInfoField.AirTemp, TrackInfoField.TimeLeft],
            bar.Where(entry => entry.SeparatorBefore).Select(entry => entry.Field));
    }

    [Fact]
    public void ASeparatorGoesWhereverNeighboursBelongToDifferentGroups()
    {
        var options = new TrackInfoOptions
        {
            FieldOrder = [TrackInfoField.Lap, TrackInfoField.TrackName, TrackInfoField.AirTemp, TrackInfoField.Wind, TrackInfoField.TimeLeft],
        };

        var separated = options.Bar().Where(entry => entry.SeparatorBefore).Select(entry => entry.Field).ToList();

        // Lap | track name | air, wind | time left | (the rest, appended in default order)
        Assert.Equal(TrackInfoField.TrackName, separated[0]);
        Assert.Equal(TrackInfoField.AirTemp, separated[1]);
        Assert.Equal(TrackInfoField.TimeLeft, separated[2]);
        Assert.False(options.Bar()[0].SeparatorBefore);
    }

    [Fact]
    public void HiddenFields_LeaveNoSeparatorBehind()
    {
        var options = new TrackInfoOptions();
        foreach (var field in new[] { TrackInfoField.AirTemp, TrackInfoField.TrackTemp, TrackInfoField.Wind, TrackInfoField.Humidity, TrackInfoField.TrackUsage })
        {
            options.SetVisible(field, false);
        }

        var bar = options.Bar();

        Assert.Equal([TrackInfoField.TrackName, TrackInfoField.Session, TrackInfoField.TimeLeft, TrackInfoField.Lap], bar.Select(entry => entry.Field));
        Assert.Equal([false, false, true, false], bar.Select(entry => entry.SeparatorBefore));
    }

    [Fact]
    public void FieldOrder_IsNormalised()
    {
        var options = new TrackInfoOptions { FieldOrder = [TrackInfoField.Lap, TrackInfoField.Lap, (TrackInfoField)42] };

        Assert.Equal(TrackInfoField.Lap, options.FieldOrder[0]);
        Assert.Equal(TrackInfoOptions.DefaultOrder.Count, options.FieldOrder.Count);
    }
}
