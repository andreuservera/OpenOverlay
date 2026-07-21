using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class StandingsBuilderTests
{
    private static SyntheticMemoryBuilder RelativeVars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("CarIdxLap", IrsdkVarType.Int, count: 4);
        builder.AddVar("CarIdxEstTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxLapDistPct", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxLastLapTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxBestLapTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxOnPitRoad", IrsdkVarType.Bool, count: 4);
        return builder;
    }

    [Fact]
    public void BuildRelative_SoloSession_StillReturnsThePlayer()
    {
        var builder = RelativeVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [3, 0, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [12.5f, 0, 0, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers = [new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" }],
            },
        };

        var rows = StandingsBuilder.BuildRelative(snapshot, session);

        var row = Assert.Single(rows);
        Assert.True(row.IsPlayer);
        Assert.Equal(0, row.GapSeconds);
    }

    [Fact]
    public void BuildRelative_CarAheadOnSameLap_ShowsNegativeGap()
    {
        var builder = RelativeVars();
        // Player (idx 0) is at 10s into the lap; car idx 1 is at 8s into the same lap, i.e. behind.
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [5, 5, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [10.0f, 8.0f, 0, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Rival", CarNumber = "42" },
                ],
            },
        };

        var rows = StandingsBuilder.BuildRelative(snapshot, session);

        var rivalRow = rows.Single(r => r.CarIdx == 1);
        // Player is 2s further into the lap than the rival => the rival is 2s behind => positive gap.
        Assert.Equal(2.0, rivalRow.GapSeconds, precision: 3);
    }

    [Fact]
    public void BuildRelative_SameLap_MismatchedReferenceLapTimes_StaysSensibleManyLapsIn()
    {
        // Regression test for the reported "gap numbers don't make sense" bug: both cars are on lap
        // 40 (same lap, genuinely close together), but have different (or, for car 1, no) recorded
        // lap times. The old per-car-reference-lap-time formula multiplied that mismatch by the lap
        // count (40 * ~90s), producing a gap of thousands of seconds for cars sitting right next to
        // each other. The fix must keep the gap tiny regardless of car 1's lap-time data.
        var builder = RelativeVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [40, 40, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [30.0f, 29.5f, 0, 0]); // 0.5s apart on the same lap
            w.SetFloatArray("CarIdxLastLapTime", [92.3f, 0, 0, 0]); // player has a lap time; rival doesn't
            w.SetFloatArray("CarIdxBestLapTime", [91.8f, 0, 0, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Rival", CarNumber = "42" },
                ],
            },
        };

        var rows = StandingsBuilder.BuildRelative(snapshot, session);

        var rivalRow = rows.Single(r => r.CarIdx == 1);
        Assert.Equal(0.5, rivalRow.GapSeconds, precision: 3);
    }

    [Fact]
    public void BuildRelative_NoReferenceLapTime_ExcludesCarsOnADifferentLap()
    {
        // Regression test for what live testing surfaced: player sitting in the garage (no lap time
        // set yet, refLapTime == 0) alongside cars actually several laps further into the race. With
        // no way to correct for the lap difference, comparing them produced a small, plausible-looking
        // but meaningless gap. Cars genuinely on the same lap should still compare normally.
        var builder = RelativeVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [0, 12, 0, 0]); // car 1 is 12 laps ahead of the player
            w.SetFloatArray("CarIdxEstTime", [1.0f, 30.5f, 0, 0]);
            // no CarIdxLastLapTime/CarIdxBestLapTime set for the player -> refLapTime stays 0
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "LapsAhead", CarNumber = "42" },
                ],
            },
        };

        var rows = StandingsBuilder.BuildRelative(snapshot, session);

        Assert.DoesNotContain(rows, r => r.CarIdx == 1);
        Assert.Contains(rows, r => r.IsPlayer);
    }

    [Fact]
    public void BuildRelative_IgnoresPaceCar()
    {
        var builder = RelativeVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [1, 0, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [1.0f, 0, 0, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Pace Car", CarIsPaceCar = 1 },
                ],
            },
        };

        var rows = StandingsBuilder.BuildRelative(snapshot, session);

        Assert.Single(rows);
    }

    private static SyntheticMemoryBuilder StandingsVars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("CarIdxPosition", IrsdkVarType.Int, count: 4);
        builder.AddVar("CarIdxClassPosition", IrsdkVarType.Int, count: 4);
        builder.AddVar("CarIdxLap", IrsdkVarType.Int, count: 4);
        builder.AddVar("CarIdxEstTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxF2Time", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxLastLapTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxBestLapTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxOnPitRoad", IrsdkVarType.Bool, count: 4);
        return builder;
    }

    [Fact]
    public void BuildStandings_NoOfficialPosition_FallsBackToTrackPositionAndStillShowsPlayer()
    {
        // Mirrors a solo/offline Test session: CarIdxPosition exists in the var table but is never
        // populated (stays 0), which is exactly what iRacing does for non-scored sessions.
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [0, 0, 0, 0]);
            w.SetIntArray("CarIdxLap", [3, 0, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [12.5f, 0, 0, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers = [new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" }],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        var row = Assert.Single(rows);
        Assert.True(row.IsPlayer);
        Assert.Equal(1, row.Position);
    }

    [Fact]
    public void BuildStandings_NoOfficialPosition_OrdersMultipleCarsByTrackPosition()
    {
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [0, 0, 0, 0]);
            w.SetIntArray("CarIdxLap", [5, 5, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [10.0f, 15.0f, 0, 0]); // car 1 is further into the lap
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Ahead", CarNumber = "9" },
                ],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows.Single(r => r.CarIdx == 1).Position);
        Assert.Equal(2, rows.Single(r => r.CarIdx == 0).Position);
    }

    [Fact]
    public void BuildStandings_MultipleClasses_FlagsMultiClassAndTracksClassPosition()
    {
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [1, 2, 0, 0]);
            w.SetIntArray("CarIdxClassPosition", [1, 1, 0, 0]); // each leads their own class
            w.SetFloatArray("CarIdxBestLapTime", [90.0f, 95.0f, 0, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7", CarClassID = 100 },
                    new DriverEntry { CarIdx = 1, UserName = "Other Class", CarNumber = "9", CarClassID = 200 },
                ],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        Assert.All(rows, r => Assert.True(r.IsMultiClass));
        var leader = rows.Single(r => r.CarIdx == 0);
        Assert.Equal("1 (1)", leader.PositionDisplay);
    }

    [Fact]
    public void BuildStandings_SingleClass_PositionDisplayIsPlainNumber()
    {
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [1, 2, 0, 0]);
            w.SetIntArray("CarIdxClassPosition", [1, 2, 0, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7", CarClassID = 100 },
                    new DriverEntry { CarIdx = 1, UserName = "Rival", CarNumber = "9", CarClassID = 100 },
                ],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        var leader = rows.Single(r => r.CarIdx == 0);
        Assert.Equal("1", leader.PositionDisplay);
    }
}
