using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class SessionProgressTests
{
    private const double SevenDays = 604_800;

    private static SyntheticMemoryBuilder Vars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Lap", IrsdkVarType.Int);
        builder.AddVar("LapCompleted", IrsdkVarType.Int);
        builder.AddVar("LapDistPct", IrsdkVarType.Float);
        builder.AddVar("LapLastLapTime", IrsdkVarType.Float);
        builder.AddVar("SessionLapsTotal", IrsdkVarType.Int);
        builder.AddVar("SessionLapsRemainEx", IrsdkVarType.Int);
        builder.AddVar("SessionTimeTotal", IrsdkVarType.Double);
        builder.AddVar("SessionTimeRemain", IrsdkVarType.Double);
        builder.AddVar("SessionTime", IrsdkVarType.Double);
        return builder;
    }

    private static TelemetrySnapshot Snapshot(
        int lap = 7, int lapsTotal = 32767, int lapsRemain = 32767, double timeTotal = SevenDays,
        double timeRemain = SevenDays, float lastLap = 0, float pct = 0.3f, double sessionTime = 0) =>
        TestSnapshotFactory.Build(Vars(), w =>
        {
            w.SetInt("Lap", lap);
            w.SetInt("LapCompleted", lap - 1);
            w.SetFloat("LapDistPct", pct);
            w.SetFloat("LapLastLapTime", lastLap);
            w.SetInt("SessionLapsTotal", lapsTotal);
            w.SetInt("SessionLapsRemainEx", lapsRemain);
            w.SetDouble("SessionTimeTotal", timeTotal);
            w.SetDouble("SessionTimeRemain", timeRemain);
            w.SetDouble("SessionTime", sessionTime);
        });

    [Fact]
    public void LapRace_ShowsTheLapOutOfTheRacesLaps()
    {
        var progress = SessionProgressBuilder.Build(Snapshot(lapsTotal: 24, lapsRemain: 18), null, 0);

        Assert.Equal("7/24", progress.LapDisplay);
    }

    [Fact]
    public void TimedRace_EstimatesTheTotalFromRecentRacingLaps()
    {
        // 6.3 laps run, 2205 s left at 90 s a lap = 24.5 more: 30.8 in all.
        var progress = SessionProgressBuilder.Build(
            Snapshot(timeTotal: 3600, timeRemain: 2205, lastLap: 120), null, recentRacingLapSeconds: 90);

        Assert.Equal("7/~30.8", progress.LapDisplay);
        Assert.Equal("0:23:15 / 1:00:00", progress.TimeDisplay);
    }

    [Fact]
    public void TimedRace_BeforeAnyRacingLap_FallsBackToTheLastLap()
    {
        var progress = SessionProgressBuilder.Build(Snapshot(timeTotal: 1800, timeRemain: 900, lastLap: 100), null, 0);

        Assert.Equal(6.3 + 9, progress.EstimatedTotalLaps!.Value, precision: 3);
        Assert.Equal("15:00 / 30:00", progress.TimeDisplay);
    }

    [Fact]
    public void TimedRace_ClockRunOut_NeverEstimatesBelowTheLapUnderWay()
    {
        var progress = SessionProgressBuilder.Build(Snapshot(timeTotal: 1800, timeRemain: 0, lastLap: 100, pct: 0.1f), null, 0);

        Assert.Equal("7/~7.0", progress.LapDisplay);
    }

    [Fact]
    public void TimedRace_NoLapTimeAnywhere_ShowsJustTheLap()
    {
        var progress = SessionProgressBuilder.Build(Snapshot(timeTotal: 1800, timeRemain: 900), null, 0);

        Assert.Equal("7", progress.LapDisplay);
    }

    [Fact]
    public void TimedRace_BeforeAnyLap_UsesTheCarsEstimatedLapTime()
    {
        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 3,
                Drivers = [new DriverEntry { CarIdx = 3, CarClassEstLapTime = 100 }],
            },
        };

        var progress = SessionProgressBuilder.Build(Snapshot(lap: 1, timeTotal: 1800, timeRemain: 1800, pct: 0), session, 0);

        Assert.Equal("1/~18.0", progress.LapDisplay);
    }

    [Fact]
    public void OpenSession_ShowsElapsedTimeOnly()
    {
        var progress = SessionProgressBuilder.Build(Snapshot(sessionTime: 581), null, 0);

        Assert.Null(progress.TotalSeconds);
        Assert.Equal("09:41", progress.TimeDisplay);
        Assert.Equal("7", progress.LapDisplay);
    }

    [Fact]
    public void Empty_ShowsDashes()
    {
        Assert.Equal("—", SessionProgress.Empty.LapDisplay);
        Assert.Equal("—", SessionProgress.Empty.TimeDisplay);
    }

    [Fact]
    public void LapRace_CoolDownLap_StillReadsTheRaceDistance()
    {
        var progress = SessionProgressBuilder.Build(Snapshot(lap: 24, lapsTotal: 23), null, 0);

        Assert.Equal("23/23", progress.LapDisplay);
    }

    [Fact]
    public void TrackInfo_LapRace_ShowsCurrentLapOverTotalLaps()
    {
        var state = TrackInfoBuilder.Build(Snapshot(lapsTotal: 23, lapsRemain: 17), session: null);

        Assert.Equal("7/23", state.LapDisplay);
    }

    [Fact]
    public void TrackInfo_TimedRace_ShowsTheEstimatedTotal()
    {
        var state = TrackInfoBuilder.Build(Snapshot(timeTotal: 1800, timeRemain: 450), session: null, recentRacingLapSeconds: 90);

        Assert.Equal("7/~11.3", state.LapDisplay);
    }

    [Fact]
    public void TrackInfo_SevenDayClock_IsNotASessionClock()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("SessionTimeRemain", IrsdkVarType.Double);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetDouble("SessionTimeRemain", SevenDays - 30));

        var state = TrackInfoBuilder.Build(snapshot, session: null, recentRacingLapSeconds: 90);

        Assert.Equal("—", state.TimeRemainingDisplay);
        Assert.Equal("—", state.LapDisplay);
    }
}
