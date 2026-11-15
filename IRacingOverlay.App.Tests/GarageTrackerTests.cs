using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;
using Xunit;

namespace IRacingOverlay.App.Tests;

public class GarageTrackerTests
{
    private const int OnTrack = 3;
    private const int NotInWorld = -1;

    private static TelemetrySnapshot Tick(double time, int[] surfaces, int sessionNum = 0)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("SessionNum", IrsdkVarType.Int);
        builder.AddVar("SessionTime", IrsdkVarType.Double);
        builder.AddVar("CarIdxTrackSurface", IrsdkVarType.Int, count: surfaces.Length);
        return TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("SessionNum", sessionNum);
            w.SetDouble("SessionTime", time);
            w.SetIntArray("CarIdxTrackSurface", surfaces);
        });
    }

    /// <summary>Cars 0..count-1 in the race, with the scoring table's laps completed per car (none
    /// listed when null).</summary>
    private static IracingSessionInfo Session(int count, int[]? lapsComplete = null, double fastest = 90, int paceCarIdx = -1) => new()
    {
        DriverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers = Enumerable.Range(0, count)
                .Select(i => new DriverEntry { CarIdx = i, UserName = $"Car {i}", CarIsPaceCar = i == paceCarIdx ? 1 : 0 })
                .ToList(),
        },
        SessionInfo = new SessionInfoSection
        {
            Sessions =
            [
                new SessionEntry
                {
                    SessionNum = 0,
                    ResultsPositions = lapsComplete is null
                        ? []
                        : lapsComplete.Select((laps, i) => new SessionResultPosition { CarIdx = i, LapsComplete = laps, FastestTime = fastest }).ToList(),
                },
            ],
        },
    };

    [Fact]
    public void CarLeavingASmallField_IsInTheGarageOnceTheFieldStaysThinner()
    {
        var tracker = new GarageTracker();
        var session = Session(3);
        tracker.Update(Tick(100, [OnTrack, OnTrack, OnTrack]), session);
        tracker.Update(Tick(101, [OnTrack, OnTrack, NotInWorld]), session);
        tracker.Update(Tick(108, [OnTrack, OnTrack, NotInWorld]), session);
        Assert.Empty(tracker.InGarage);

        tracker.Update(Tick(112, [OnTrack, OnTrack, NotInWorld]), session);

        Assert.Equal([2], tracker.InGarage);
    }

    [Fact]
    public void CarBackOnTrack_LeavesTheGarage()
    {
        var tracker = new GarageTracker();
        var session = Session(3);
        tracker.Update(Tick(100, [OnTrack, OnTrack, OnTrack]), session);
        tracker.Update(Tick(101, [OnTrack, OnTrack, NotInWorld]), session);
        tracker.Update(Tick(112, [OnTrack, OnTrack, NotInWorld]), session);
        Assert.Single(tracker.InGarage);

        tracker.Update(Tick(113, [OnTrack, OnTrack, OnTrack]), session);

        Assert.Empty(tracker.InGarage);
    }

    [Fact]
    public void CarHiddenByTheMaxCarsLimit_StillLapping_IsNotInTheGarage()
    {
        // The client renders two cars at most: as car 2 drops out of view, car 3 comes in, so the
        // field never looks thinner. Car 2 keeps completing laps on the scoring table.
        var tracker = new GarageTracker();
        tracker.Update(Tick(100, [OnTrack, OnTrack, OnTrack, NotInWorld]), Session(4, [10, 10, 10, 10]));
        tracker.Update(Tick(101, [OnTrack, OnTrack, NotInWorld, OnTrack]), Session(4, [10, 10, 10, 10]));
        tracker.Update(Tick(190, [OnTrack, OnTrack, NotInWorld, OnTrack]), Session(4, [11, 11, 11, 11]));
        tracker.Update(Tick(280, [OnTrack, OnTrack, NotInWorld, OnTrack]), Session(4, [12, 12, 12, 12]));
        tracker.Update(Tick(370, [OnTrack, OnTrack, NotInWorld, OnTrack]), Session(4, [13, 13, 13, 13]));

        Assert.Empty(tracker.InGarage);
    }

    [Fact]
    public void CarHiddenFromAFullClient_WithNoLapsCounted_IsInTheGarage()
    {
        // Same full client, but car 2 has stopped scoring laps: longer than a slow lap plus a stop.
        var tracker = new GarageTracker();
        tracker.Update(Tick(100, [OnTrack, OnTrack, OnTrack, NotInWorld]), Session(4, [10, 10, 10, 10]));
        tracker.Update(Tick(101, [OnTrack, OnTrack, NotInWorld, OnTrack]), Session(4, [10, 10, 10, 10]));
        tracker.Update(Tick(190, [OnTrack, OnTrack, NotInWorld, OnTrack]), Session(4, [11, 11, 10, 11]));
        Assert.Empty(tracker.InGarage);

        tracker.Update(Tick(300, [OnTrack, OnTrack, NotInWorld, OnTrack]), Session(4, [12, 12, 10, 12]));

        Assert.Equal([2], tracker.InGarage);
    }

    [Fact]
    public void PaceCarLeaving_DoesNotThinTheField()
    {
        // Car 3 is hidden by the client's limit; the pace car (car 1) pulling off must not make it
        // look parked.
        var tracker = new GarageTracker();
        var session = Session(4, [5, 0, 5, 5], paceCarIdx: 1);
        tracker.Update(Tick(100, [OnTrack, OnTrack, OnTrack, NotInWorld]), session);
        tracker.Update(Tick(101, [OnTrack, NotInWorld, OnTrack, NotInWorld]), session);
        tracker.Update(Tick(130, [OnTrack, NotInWorld, OnTrack, NotInWorld]), session);

        Assert.Empty(tracker.InGarage);
    }

    [Fact]
    public void BriefBlinkOutOfTheWorld_IsNotTheGarage()
    {
        var tracker = new GarageTracker();
        var session = Session(3, [5, 5, 5]);
        tracker.Update(Tick(100, [OnTrack, OnTrack, NotInWorld]), session);
        tracker.Update(Tick(400, [OnTrack, OnTrack, OnTrack]), session);
        tracker.Update(Tick(401, [OnTrack, OnTrack, NotInWorld]), session);
        tracker.Update(Tick(402, [OnTrack, OnTrack, NotInWorld]), session);

        Assert.Empty(tracker.InGarage);
    }

    [Fact]
    public void NewSession_ClearsTheGarage()
    {
        var tracker = new GarageTracker();
        var session = Session(3);
        tracker.Update(Tick(100, [OnTrack, OnTrack, OnTrack]), session);
        tracker.Update(Tick(101, [OnTrack, OnTrack, NotInWorld]), session);
        tracker.Update(Tick(112, [OnTrack, OnTrack, NotInWorld]), session);
        Assert.Single(tracker.InGarage);

        tracker.Update(Tick(5, [OnTrack, OnTrack, NotInWorld], sessionNum: 1), session);

        Assert.Empty(tracker.InGarage);
    }
}
