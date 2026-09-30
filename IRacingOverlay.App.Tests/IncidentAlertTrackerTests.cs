using IRacingOverlay.App.ViewModels;
using Xunit;

namespace IRacingOverlay.App.Tests;

public class IncidentAlertTrackerTests
{
    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void FirstReading_IsTheBaselineNotAnIncident()
    {
        var tracker = new IncidentAlertTracker();

        Assert.Null(tracker.Observe(6, At(0)));
        Assert.Null(tracker.Observe(6, At(1)));
    }

    [Theory]
    [InlineData(1, "1x OFF TRACK")]
    [InlineData(2, "2x LOSS OF CONTROL")]
    [InlineData(4, "4x CONTACT")]
    public void EachIncidentValue_NamesWhatHappened(int points, string expected)
    {
        var tracker = new IncidentAlertTracker();
        tracker.Observe(3, At(0));

        Assert.Equal(expected, tracker.Observe(3 + points, At(10))!.Display);
    }

    [Fact]
    public void OffTrackUpgradedToASpin_ReadsAsOneLossOfControl()
    {
        var tracker = new IncidentAlertTracker();
        tracker.Observe(0, At(0));

        Assert.Equal("1x OFF TRACK", tracker.Observe(1, At(10))!.Display);
        Assert.Equal("2x LOSS OF CONTROL", tracker.Observe(2, At(11.5))!.Display);
        Assert.Equal("4x CONTACT", tracker.Observe(4, At(12))!.Display);
    }

    [Fact]
    public void IncidentsFarApart_AreSeparate()
    {
        var tracker = new IncidentAlertTracker();
        tracker.Observe(0, At(0));
        tracker.Observe(1, At(10));

        Assert.Equal("1x OFF TRACK", tracker.Observe(2, At(30))!.Display);
    }

    [Fact]
    public void ATotalNoUpgradeCanReach_StartsANewIncident()
    {
        var tracker = new IncidentAlertTracker();
        tracker.Observe(0, At(0));
        tracker.Observe(1, At(10));

        // 1x then +2 is 3, which no single incident is worth: a separate loss of control.
        Assert.Equal("2x LOSS OF CONTROL", tracker.Observe(3, At(11))!.Display);
    }

    [Fact]
    public void CountDroppingOrReset_TakesANewBaselineSilently()
    {
        var tracker = new IncidentAlertTracker();
        tracker.Observe(8, At(0));

        Assert.Null(tracker.Observe(0, At(1))); // new session
        Assert.Equal("1x OFF TRACK", tracker.Observe(1, At(20))!.Display);

        tracker.Reset();
        Assert.Null(tracker.Observe(5, At(40))); // reconnected mid-session
    }
}
