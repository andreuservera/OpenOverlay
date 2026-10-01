using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class IncidentReportTests
{
    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Theory]
    [InlineData(0x0301u, IncidentKind.LossOfControl, 2)]
    [InlineData(0x0202u, IncidentKind.OffTrack, 1)]
    [InlineData(0x0104u, IncidentKind.WallContact, 0)]
    [InlineData(0x0305u, IncidentKind.WallContact, 2)]
    [InlineData(0x0107u, IncidentKind.CarContact, 0)]
    [InlineData(0x0408u, IncidentKind.CarContact, 4)]
    public void Decode_ReadsWhatHappenedAndWhatItCost(uint raw, IncidentKind kind, int points)
    {
        var report = IncidentReport.Decode(raw, sequence: 1)!;

        Assert.Equal(kind, report.Kind);
        Assert.Equal(points, report.Points);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x0300u)] // a cost but no report
    [InlineData(0x0009u)] // a report byte this build doesn't know
    public void Decode_NothingUsable_IsNull(uint raw)
    {
        Assert.Null(IncidentReport.Decode(raw, sequence: 1));
    }

    [Fact]
    public void Latch_KeepsTheLastReport_AndNumbersEachNewOne()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("PlayerIncidents", IrsdkVarType.BitField);
        var latch = new IncidentReportLatch();
        void Tick(uint raw) => latch.Observe(TestSnapshotFactory.Build(builder, w => w.SetBitField("PlayerIncidents", raw)));

        Tick(0);
        Assert.Null(latch.Latest);

        Tick(0x0305);
        var first = latch.Latest!;
        Assert.Equal(IncidentKind.WallContact, first.Kind);

        Tick(0x0305); // still up: the same incident
        Tick(0);      // one-tick log: gone again, the report stays latched
        Assert.Same(first, latch.Latest);

        Tick(0x0305); // the same kind of incident, again
        Assert.NotEqual(first.Sequence, latch.Latest!.Sequence);
    }

    [Fact]
    public void Tracker_NamesTheIncidentFromTheSimsReport()
    {
        var tracker = new IncidentAlertTracker();
        tracker.Observe(3, At(0));

        var alert = tracker.Observe(5, At(10), new IncidentReport(IncidentKind.WallContact, 2, 1))!;

        Assert.Equal("2x WALL CONTACT", alert.Display);
    }

    [Fact]
    public void Tracker_LightContact_IsNamedWithoutACountChange()
    {
        var tracker = new IncidentAlertTracker();
        tracker.Observe(3, At(0));

        var alert = tracker.Observe(3, At(10), new IncidentReport(IncidentKind.CarContact, 0, 1))!;

        Assert.Equal("0x CAR CONTACT", alert.Display);
        Assert.False(alert.IsCorrection);
    }

    [Fact]
    public void Tracker_ReportArrivingJustAfterTheCount_CorrectsTheName()
    {
        var tracker = new IncidentAlertTracker();
        tracker.Observe(0, At(0));

        Assert.Equal("2x LOSS OF CONTROL", tracker.Observe(2, At(10))!.Display);
        var correction = tracker.Observe(2, At(10.5), new IncidentReport(IncidentKind.WallContact, 2, 1))!;

        Assert.True(correction.IsCorrection);
        Assert.Equal("2x WALL CONTACT", correction.Display);
    }

    [Fact]
    public void Tracker_AReportIsOnlyUsedOnce()
    {
        var tracker = new IncidentAlertTracker();
        var report = new IncidentReport(IncidentKind.WallContact, 2, 1);
        tracker.Observe(0, At(0));
        tracker.Observe(2, At(10), report);

        // Same report still latched, new incident: nothing says what this one was.
        Assert.Equal("1x OFF TRACK", tracker.Observe(3, At(60), report)!.Display);
    }

    [Fact]
    public void Tracker_ChainUpgrade_TakesTheUpgradedReport()
    {
        var tracker = new IncidentAlertTracker();
        tracker.Observe(0, At(0));

        Assert.Equal("1x OFF TRACK", tracker.Observe(1, At(10), new IncidentReport(IncidentKind.OffTrack, 1, 1))!.Display);
        Assert.Equal("2x LOSS OF CONTROL", tracker.Observe(2, At(11), new IncidentReport(IncidentKind.LossOfControl, 2, 2))!.Display);
    }
}
