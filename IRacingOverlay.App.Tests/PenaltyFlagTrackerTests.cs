using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class PenaltyFlagTrackerTests
{
    private static readonly IracingSessionInfo Session = new()
    {
        DriverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                new DriverEntry { CarIdx = 1, UserName = "Rival", CarNumber = "8" },
            ],
        },
    };

    private static TelemetrySnapshot Snapshot(uint rivalFlags)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("CarIdxSessionFlags", IrsdkVarType.BitField, count: 2);
        return TestSnapshotFactory.Build(builder, w => w.SetIntArray("CarIdxSessionFlags", [0, unchecked((int)rivalFlags)]));
    }

    [Fact]
    public void Update_ReportsOnlyChanges()
    {
        var tracker = new PenaltyFlagTracker();

        Assert.Empty(tracker.Update(Snapshot(0), Session));

        var on = Assert.Single(tracker.Update(Snapshot(0x00010000 | 0x00100000), Session));
        Assert.Equal(1, on.Driver.CarIdx);
        Assert.True(on.Penalties.Black);
        Assert.True(on.Penalties.Meatball);

        Assert.Empty(tracker.Update(Snapshot(0x00010000 | 0x00100000), Session));

        var off = Assert.Single(tracker.Update(Snapshot(0x00100000), Session));
        Assert.False(off.Penalties.Black);
        Assert.True(off.Penalties.Meatball);
    }
}
