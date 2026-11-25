using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;
using Xunit;

namespace IRacingOverlay.App.Tests;

public class PitStopTrackerTests
{
    private const int InWorld = 3;
    private const int NotInWorld = -1;

    private static TelemetrySnapshot Tick(double time, bool onPitRoad, int lap, int surface = InWorld, int sessionNum = 0)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("SessionNum", IrsdkVarType.Int);
        builder.AddVar("SessionTime", IrsdkVarType.Double);
        builder.AddVar("CarIdxOnPitRoad", IrsdkVarType.Bool, count: 1);
        builder.AddVar("CarIdxLap", IrsdkVarType.Int, count: 1);
        builder.AddVar("CarIdxTrackSurface", IrsdkVarType.Int, count: 1);
        return TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("SessionNum", sessionNum);
            w.SetDouble("SessionTime", time);
            w.SetBoolArray("CarIdxOnPitRoad", [onPitRoad]);
            w.SetIntArray("CarIdxLap", [lap]);
            w.SetIntArray("CarIdxTrackSurface", [surface]);
        });
    }

    [Fact]
    public void StopFromTrack_RecordsEntryLapAndPitLaneTime()
    {
        var tracker = new PitStopTracker();
        tracker.Update(Tick(100, onPitRoad: false, lap: 23), null);
        tracker.Update(Tick(110, onPitRoad: true, lap: 24), null);
        Assert.Empty(tracker.LastStops);

        tracker.Update(Tick(150, onPitRoad: true, lap: 24), null);
        tracker.Update(Tick(188, onPitRoad: false, lap: 24), null);

        Assert.Equal(new PitStop(24, 78), tracker.LastStops[0]);
    }

    [Fact]
    public void LeavingThePitsWithoutHavingDriven_IsNotAStop()
    {
        var tracker = new PitStopTracker();
        tracker.Update(Tick(5, onPitRoad: true, lap: 0), null);
        tracker.Update(Tick(40, onPitRoad: false, lap: 0), null);

        Assert.Empty(tracker.LastStops);
    }

    [Fact]
    public void TowedCar_IsNotAStop()
    {
        var tracker = new PitStopTracker();
        tracker.Update(Tick(100, onPitRoad: false, lap: 5), null);
        tracker.Update(Tick(101, onPitRoad: false, lap: 5, surface: NotInWorld), null);
        tracker.Update(Tick(130, onPitRoad: true, lap: 5), null);
        tracker.Update(Tick(160, onPitRoad: false, lap: 5), null);

        Assert.Empty(tracker.LastStops);
    }

    [Fact]
    public void NewSession_ClearsStops()
    {
        var tracker = new PitStopTracker();
        tracker.Update(Tick(100, onPitRoad: false, lap: 5), null);
        tracker.Update(Tick(110, onPitRoad: true, lap: 5), null);
        tracker.Update(Tick(170, onPitRoad: false, lap: 5), null);
        Assert.Single(tracker.LastStops);

        tracker.Update(Tick(10, onPitRoad: false, lap: 0, sessionNum: 1), null);

        Assert.Empty(tracker.LastStops);
    }

    [Fact]
    public void Row_FormatsLapAndMinutesSeconds()
    {
        var row = new StandingsRow
        {
            CarIdx = 0, Position = 1, ClassPosition = 1, Name = "A", CarNumber = "1", IsPlayer = false,
            OnPitRoad = false, CurrentLap = 30, LastLapTime = 0, BestLapTime = 0, IsMultiClass = false,
            IRating = 0, LicString = "", IRatingDelta = 0, IsSessionFastestLap = false, GapToLeaderSeconds = 0,
            LastPitStop = new PitStop(24, 77.6),
        };

        Assert.True(row.HasLastPitStop);
        Assert.Equal("L24", row.LastPitLapDisplay);
        Assert.Equal("01:18", row.LastPitDurationDisplay);
    }

    [Fact]
    public void AfterAStop_IsOnAnOutLapUntilItCompletesTheLapItLeftOn()
    {
        var tracker = new PitStopTracker();
        tracker.Update(Tick(100, onPitRoad: false, lap: 23), null);
        tracker.Update(Tick(110, onPitRoad: true, lap: 24), null);
        Assert.Empty(tracker.OnOutLap); // PIT, not OUT

        tracker.Update(Tick(150, onPitRoad: false, lap: 24), null);
        Assert.Equal([0], tracker.OnOutLap);
        tracker.Update(Tick(200, onPitRoad: false, lap: 24), null);
        Assert.Equal([0], tracker.OnOutLap);

        tracker.Update(Tick(240, onPitRoad: false, lap: 25), null);
        Assert.Empty(tracker.OnOutLap);
    }

    [Fact]
    public void OutOfTheGarage_IsOnAnOutLap()
    {
        // Clicking Drive puts the car in its stall: out of the world, then in the stall, then out.
        var tracker = new PitStopTracker();
        tracker.Update(Tick(5, onPitRoad: false, lap: -1, surface: NotInWorld), null);
        tracker.Update(Tick(10, onPitRoad: false, lap: 3, surface: 1), null); // in its stall
        tracker.Update(Tick(30, onPitRoad: false, lap: 3), null);

        Assert.Equal([0], tracker.OnOutLap);
    }

    [Fact]
    public void AttachingToACarAlreadyOnTrack_IsNotAnOutLap()
    {
        var tracker = new PitStopTracker();
        tracker.Update(Tick(100, onPitRoad: false, lap: 5), null);
        tracker.Update(Tick(101, onPitRoad: false, lap: 5), null);

        Assert.Empty(tracker.OnOutLap);
    }

    [Fact]
    public void OutLap_HiddenWhileOutOfView_BackIfStillOnIt()
    {
        var tracker = new PitStopTracker();
        tracker.Update(Tick(100, onPitRoad: true, lap: 4), null);
        tracker.Update(Tick(110, onPitRoad: false, lap: 4), null);
        tracker.Update(Tick(120, onPitRoad: false, lap: -1, surface: NotInWorld), null);
        Assert.Empty(tracker.OnOutLap);

        tracker.Update(Tick(130, onPitRoad: false, lap: 4), null);
        Assert.Equal([0], tracker.OnOutLap);
    }

    [Fact]
    public void NewSession_ClearsTheOutLap()
    {
        var tracker = new PitStopTracker();
        tracker.Update(Tick(100, onPitRoad: true, lap: 4), null);
        tracker.Update(Tick(110, onPitRoad: false, lap: 4), null);

        tracker.Update(Tick(5, onPitRoad: false, lap: 4, sessionNum: 1), null);

        Assert.Empty(tracker.OnOutLap);
    }
}
