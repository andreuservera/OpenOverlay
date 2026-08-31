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

    /// <summary>The real rows out of a Relative build. The builder also emits reserved placeholder
    /// slots to hold the widget's height steady, which these tests aren't about.</summary>
    private static List<RelativeRow> RelativeRows(
        TelemetrySnapshot snapshot, IracingSessionInfo session, int maxEachSide = 4) =>
        StandingsBuilder.BuildRelative(snapshot, session, maxEachSide).OfType<RelativeRow>().ToList();

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

        var rows = RelativeRows(snapshot, session);

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

        var rows = RelativeRows(snapshot, session);

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

        var rows = RelativeRows(snapshot, session);

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

        var rows = RelativeRows(snapshot, session);

        Assert.DoesNotContain(rows, r => r.CarIdx == 1);
        Assert.Contains(rows, r => r.IsPlayer);
    }

    [Fact]
    public void BuildRelative_PlayerOnFirstLap_StillShowsNearbyCarUsingItsRecordedLapTime()
    {
        // Regression test for the reported "in the first lap it doesn't pick anything" bug: the
        // player hasn't completed a lap yet (no CarIdxLastLapTime/CarIdxBestLapTime of their own),
        // but another car in the session already has one. The reference lap time should fall back to
        // that car's, rather than leaving refLapTime at 0 and excluding everyone on a different lap.
        var builder = RelativeVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [0, 15, 0, 0]); // rival has circulated many more laps
            w.SetFloatArray("CarIdxEstTime", [10.0f, 10.5f, 0, 0]); // but is right next to the player
            w.SetFloatArray("CarIdxLastLapTime", [0, 90.0f, 0, 0]); // only the rival has a lap time
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

        var rows = RelativeRows(snapshot, session);

        var rivalRow = rows.Single(r => r.CarIdx == 1);
        Assert.Equal(-0.5, rivalRow.GapSeconds, precision: 3);
    }

    [Fact]
    public void BuildRelative_PracticeSession_HighLapCountDifference_DoesNotProduceMultiLapGap()
    {
        // Regression test for the reported "very large number of seconds" bug: in Practice, cars
        // don't start together, so a car dozens of laps ahead in count can still be right next to the
        // player on track. The old model multiplied the raw lap-count difference by a lap time,
        // producing gaps of thousands of seconds for cars that were genuinely side by side.
        var builder = RelativeVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [2, 40, 0, 0]); // rival is 38 laps further into the session
            w.SetFloatArray("CarIdxEstTime", [12.0f, 11.0f, 0, 0]); // but only 1s away on track
            w.SetFloatArray("CarIdxLastLapTime", [90.0f, 90.0f, 0, 0]);
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

        var rows = RelativeRows(snapshot, session);

        var rivalRow = rows.Single(r => r.CarIdx == 1);
        Assert.Equal(1.0, rivalRow.GapSeconds, precision: 3);
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

        var rows = RelativeRows(snapshot, session);

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
    public void BuildStandings_GhostAiWithAssignedPositionButNeverStarted_IsExcluded()
    {
        // Regression test for what live testing surfaced: a solo Test session's placeholder AI
        // roster all sat at CarIdxLap == -1 (iRacing's "never left the garage" sentinel) but still
        // carried an assigned CarIdxPosition, which let them slip through as "eligible" and show up
        // as a full grid of cars all tied on an identical, meaningless gap to the actual player.
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [1, 2, 3, 0]);
            w.SetIntArray("CarIdxLap", [5, -1, -1, 0]);
            w.SetFloatArray("CarIdxEstTime", [12.5f, 0, 0, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Ghost1", CarNumber = "9" },
                    new DriverEntry { CarIdx = 2, UserName = "Ghost2", CarNumber = "11" },
                ],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        var row = Assert.Single(rows);
        Assert.True(row.IsPlayer);
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
        // Position/class position now come from the continuous CarIdxLap+CarIdxEstTime ordering
        // (see BuildStandings doc comment), not the quantized official CarIdxPosition/ClassPosition —
        // car 0 is further along the same lap, so it leads overall and leads its own (only) class member.
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [5, 5, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [20.0f, 15.0f, 0, 0]);
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
        Assert.Equal("1", leader.PositionDisplay);
    }

    [Fact]
    public void BuildStandings_ClassShortNameBlank_FallsBackToCarName()
    {
        // Fixed/spec series (one car model per "class", e.g. Porsche Cup) leave CarClassShortName
        // blank in iRacing's own YAML — the car's own name is the only informative label available.
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [5, 5]);
            w.SetFloatArray("CarIdxEstTime", [20.0f, 15.0f]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7", CarClassID = 100, CarClassShortName = "", CarScreenNameShort = "Porsche Cup" },
                    new DriverEntry { CarIdx = 1, UserName = "GT3 Driver", CarNumber = "9", CarClassID = 200, CarClassShortName = "GT3", CarScreenNameShort = "911 GT3 R" },
                ],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        Assert.Equal("Porsche Cup", rows.Single(r => r.CarIdx == 0).CarClassName);
        Assert.Equal("GT3", rows.Single(r => r.CarIdx == 1).CarClassName);
    }

    [Fact]
    public void BuildStandings_SingleClass_PositionDisplayIsPlainNumber()
    {
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [5, 5, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [20.0f, 15.0f, 0, 0]);
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

    [Fact]
    public void BuildStandings_UpdatesContinuouslyMidLap_NotJustAtLapBoundaries()
    {
        // Regression test for the reported "standings only updates when finishing a lap" bug: gap
        // and order must change as CarIdxEstTime changes mid-lap, not just when CarIdxLap increments.
        var builder = StandingsVars();
        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Rival", CarNumber = "9" },
                ],
            },
        };

        // Rival (car 1) is out front leading in both snapshots; the player (car 0) is the one whose
        // gap-to-leader should shrink as they close in, mid-lap, with no lap boundary crossed.
        var early = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [3, 3, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [10.0f, 20.0f, 0, 0]); // player 10s behind the rival
        });
        var laterSameLap = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [3, 3, 0, 0]); // still the same lap
            w.SetFloatArray("CarIdxEstTime", [18.0f, 20.0f, 0, 0]); // player has closed to 2s behind
        });

        var earlyPlayer = StandingsBuilder.BuildStandings(early, session).Single(r => r.CarIdx == 0);
        var laterPlayer = StandingsBuilder.BuildStandings(laterSameLap, session).Single(r => r.CarIdx == 0);

        Assert.Equal(10.0, earlyPlayer.GapToLeaderSeconds, precision: 3);
        Assert.Equal(2.0, laterPlayer.GapToLeaderSeconds, precision: 3);
    }

    [Fact]
    public void BuildStandings_IncludesIRatingAndLicenseFromDriverInfo()
    {
        var builder = StandingsVars();
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
                Drivers = [new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7", IRating = 3250, LicString = "A 4.32" }],
            },
        };

        var row = Assert.Single(StandingsBuilder.BuildStandings(snapshot, session));

        Assert.Equal(3250, row.IRating);
        Assert.Equal("3.3k", row.IRatingDisplay);
        Assert.Equal("A 4.32", row.LicStringDisplay);
    }

    [Fact]
    public void BuildStandings_TiedTrackPositionAtRaceStart_OrdersByOfficialGridPosition()
    {
        // Regression test for what live testing surfaced: right at a race's start, every car can
        // have identical (Lap 0, EstTime 0) track position — a genuine driver who started last in
        // their class was showing ahead of faster-starting classmates purely because
        // OrderByDescending's stable-sort tie-break fell back to DriverInfo's roster order, which has
        // nothing to do with the actual starting grid. Official CarIdxPosition — assigned at grid
        // formation, before any lap timing exists — must break the tie correctly instead.
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [3, 1, 2, 0]); // player is officially last of the three
            w.SetIntArray("CarIdxLap", [0, 0, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [0, 0, 0, 0]); // identical track position for everyone
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    // Roster order deliberately puts the player first — the bug used to let this
                    // roster order leak through as race order despite the player starting last.
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "PoleSitter", CarNumber = "1" },
                    new DriverEntry { CarIdx = 2, UserName = "SecondPlace", CarNumber = "2" },
                ],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        Assert.Equal(3, rows.Single(r => r.CarIdx == 0).Position); // player last
        Assert.Equal(1, rows.Single(r => r.CarIdx == 1).Position);
        Assert.Equal(2, rows.Single(r => r.CarIdx == 2).Position);
    }

    [Fact]
    public void BuildStandings_MarksSessionFastestLapAcrossAllCars_NotJustLeader()
    {
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [10, 10, 10, 0]);
            w.SetFloatArray("CarIdxEstTime", [30.0f, 20.0f, 10.0f, 0]); // car 0 leads on track
            // Car 2 (running P3) actually set the fastest lap of the session, not the leader.
            w.SetFloatArray("CarIdxBestLapTime", [92.0f, 91.0f, 89.5f, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Leader", CarNumber = "1" },
                    new DriverEntry { CarIdx = 1, UserName = "Second", CarNumber = "2" },
                    new DriverEntry { CarIdx = 2, UserName = "FastestLap", CarNumber = "3" },
                ],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        Assert.True(rows.Single(r => r.CarIdx == 2).IsSessionFastestLap);
        Assert.False(rows.Single(r => r.CarIdx == 0).IsSessionFastestLap);
        Assert.False(rows.Single(r => r.CarIdx == 1).IsSessionFastestLap);
    }

    private static IracingSessionInfo QualifyingSession(DriverInfoSection driverInfo) => new()
    {
        DriverInfo = driverInfo,
        SessionInfo = new SessionInfoSection
        {
            CurrentSessionNum = 0,
            Sessions = [new SessionEntry { SessionNum = 0, SessionType = "Lone Qualify" }],
        },
    };

    private static IracingSessionInfo PracticeSession(DriverInfoSection driverInfo) => new()
    {
        DriverInfo = driverInfo,
        SessionInfo = new SessionInfoSection
        {
            CurrentSessionNum = 0,
            Sessions = [new SessionEntry { SessionNum = 0, SessionType = "Practice" }],
        },
    };

    [Fact]
    public void BuildStandings_Qualifying_IncludesDriverParkedInPitsWithATime()
    {
        // Regression test for the reported bug: once a driver finishes their timed qualifying lap
        // and parks back in their pit stall, iRacing can report their CarIdxLap back at the "never
        // left the garage" sentinel (-1) — the normal race eligibility check would exclude them
        // entirely even though they've genuinely set a lap time and belong on the grid.
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [0, 0, 0, 0]);
            w.SetIntArray("CarIdxLap", [2, -1, 0, 0]); // car 1 parked back in the pits, "reset" to -1
            w.SetFloatArray("CarIdxEstTime", [15.0f, 0, 0, 0]);
            w.SetFloatArray("CarIdxBestLapTime", [92.0f, 90.5f, 0, 0]); // car 1 is actually faster
        });

        var driverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                new DriverEntry { CarIdx = 1, UserName = "ParkedInPits", CarNumber = "9" },
            ],
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, QualifyingSession(driverInfo));

        Assert.Equal(2, rows.Count);
        var parked = rows.Single(r => r.CarIdx == 1);
        var player = rows.Single(r => r.CarIdx == 0);
        Assert.Equal(1, parked.Position); // faster lap -> P1 despite being parked
        Assert.Equal(2, player.Position);
    }

    [Fact]
    public void BuildStandings_Qualifying_DriverWithNoTimeYetSortsLastButStillAppears()
    {
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [1, 0, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [5.0f, 0, 0, 0]);
            w.SetFloatArray("CarIdxBestLapTime", [90.0f, 0, 0, 0]); // car 1 hasn't set a time yet
        });

        var driverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                new DriverEntry { CarIdx = 1, UserName = "NoTimeYet", CarNumber = "9" },
            ],
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, QualifyingSession(driverInfo));

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows.Single(r => r.CarIdx == 0).Position);
        Assert.Equal(2, rows.Single(r => r.CarIdx == 1).Position);
    }

    [Fact]
    public void BuildStandings_Qualifying_ParkedDriverKeepsTimeAcrossTicksEvenWhenTelemetryZeroesOut()
    {
        // Regression test for what live testing surfaced even after the previous fix: iRacing doesn't
        // just reset a parked car's CarIdxLap — it can drop CarIdxBestLapTime/CarIdxLastLapTime back
        // to 0 for that car too, on a *later* tick, well after the fast lap was genuinely recorded.
        // Reading only the current tick can't tell "never set a time" apart from "set a time, now
        // parked" — the tracker must remember the real number across ticks.
        var builder = StandingsVars();
        var driverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                new DriverEntry { CarIdx = 1, UserName = "ParkedLater", CarNumber = "9" },
            ],
        };
        var session = QualifyingSession(driverInfo);
        var tracker = new SessionBestLapTracker();

        var duringHotLap = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [1, 2, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [5.0f, 5.0f, 0, 0]);
            w.SetFloatArray("CarIdxBestLapTime", [95.0f, 90.5f, 0, 0]); // car 1 sets a fast lap
        });
        StandingsBuilder.BuildStandings(duringHotLap, session, tracker);

        var parkedInPits = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [1, -1, 0, 0]); // car 1 now parked, lap reset
            w.SetFloatArray("CarIdxEstTime", [8.0f, 0, 0, 0]);
            w.SetFloatArray("CarIdxBestLapTime", [95.0f, 0, 0, 0]); // and its best lap telemetry zeroed out
        });

        var rows = StandingsBuilder.BuildStandings(parkedInPits, session, tracker);

        Assert.Equal(2, rows.Count);
        var parked = rows.Single(r => r.CarIdx == 1);
        Assert.Equal(1, parked.Position); // still ranked on its earlier, now-cached fast lap
        Assert.Equal(90.5, parked.BestLapTime, precision: 3);
    }

    [Fact]
    public void BuildStandings_Practice_RanksByFastestLapLikeQualifying()
    {
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [3, -1, 0, 0]); // car 1 parked after a fast lap
            w.SetFloatArray("CarIdxEstTime", [10.0f, 0, 0, 0]);
            w.SetFloatArray("CarIdxBestLapTime", [92.0f, 89.0f, 0, 0]);
        });

        var driverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                new DriverEntry { CarIdx = 1, UserName = "FastInPits", CarNumber = "9" },
            ],
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, PracticeSession(driverInfo));

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows.Single(r => r.CarIdx == 1).Position);
        Assert.Equal(2, rows.Single(r => r.CarIdx == 0).Position);
    }

    [Fact]
    public void ComputeStrengthOfField_CountsEveryDriverInRosterRegardlessOfTrackStatus()
    {
        // SOF describes the whole lobby, not just whoever's currently on track — a driver sitting in
        // the pits (or one who hasn't yet been marked "started" by BuildStandings' own eligibility
        // logic) must still count, since iRating is a per-driver roster fact, not live telemetry.
        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7", IRating = 2500 },
                    new DriverEntry { CarIdx = 1, UserName = "InPits", CarNumber = "9", IRating = 2500 },
                    new DriverEntry { CarIdx = 2, UserName = "PaceCar", CarIsPaceCar = 1, IRating = 9999 },
                ],
            },
        };

        var sof = StandingsBuilder.ComputeStrengthOfField(session);

        Assert.Equal(2500, sof, precision: 3);
    }

    [Fact]
    public void BuildStandings_JoiningMidRaceWithNoLapTimeOfOwn_StillRanksLapsAheadFirst()
    {
        // Regression test for "open the overlay after the race started and the order is nonsense".
        // The player (idx 0) has just joined: no last lap, no best lap, still on lap 0. The other
        // three have been racing for laps. The order used to be laps * referenceLapTime + estTime,
        // and with the player having no lap time of their own the reference fell back to an
        // arbitrary car in the array — here car 3's quick 60s lap. That made 8 * 60 + 0 = 480 for
        // the leader rank below 7 * 60 + 95 = 515 for the car a lap down, flipping them.
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [0, 0, 0, 0]);
            w.SetIntArray("CarIdxLap", [0, 8, 7, 7]);
            // Car 1 has just crossed the line onto lap 8; car 2 is nearly all the way round lap 7.
            w.SetFloatArray("CarIdxEstTime", [0f, 1.0f, 95.0f, 40.0f]);
            w.SetFloatArray("CarIdxLastLapTime", [0f, 100.0f, 98.0f, 60.0f]);
            w.SetFloatArray("CarIdxBestLapTime", [0f, 99.0f, 97.0f, 60.0f]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Leader", CarNumber = "1" },
                    new DriverEntry { CarIdx = 2, UserName = "Second", CarNumber = "2" },
                    new DriverEntry { CarIdx = 3, UserName = "Third", CarNumber = "3" },
                ],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        // Lap 8 outranks lap 7 no matter how far round lap 7 the next car is, and the late-joining
        // player on lap 0 is last.
        Assert.Equal([1, 2, 3, 0], rows.Select(r => r.CarIdx));
    }

    [Fact]
    public void BuildStandings_NoLapTimesAnywhereYet_StillRanksLapsAheadFirst()
    {
        // The degenerate version of the same bug: with no recorded lap time anywhere the reference
        // was zero, which collapsed the sort to within-lap position only — laps stopped counting
        // entirely, so a car on lap 1 that was further round the track outranked the lap-4 leader.
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [0, 0, 0, 0]);
            w.SetIntArray("CarIdxLap", [4, 1, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [5.0f, 80.0f, 0, 0]);
            w.SetFloatArray("CarIdxLastLapTime", [0f, 0f, 0f, 0f]);
            w.SetFloatArray("CarIdxBestLapTime", [0f, 0f, 0f, 0f]);
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

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        Assert.Equal([0, 1], rows.Select(r => r.CarIdx));
    }

    [Fact]
    public void BuildStandings_MultiClass_FasterClassLapTimeDoesNotOutrankACarALapAhead()
    {
        // Same root cause, permanent rather than transient: in multiclass the reference lap time can
        // legitimately be far shorter than a slower class's lap, so laps * reference stopped
        // dominating even once the player had a lap time of their own.
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [0, 0, 0, 0]);
            w.SetIntArray("CarIdxLap", [10, 9, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [2.0f, 110.0f, 0, 0]);
            // Player runs the fast class (90s); the car a lap down runs a 115s class.
            w.SetFloatArray("CarIdxLastLapTime", [90.0f, 115.0f, 0f, 0f]);
            w.SetFloatArray("CarIdxBestLapTime", [90.0f, 115.0f, 0f, 0f]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7", CarClassID = 100 },
                    new DriverEntry { CarIdx = 1, UserName = "Slower", CarNumber = "42", CarClassID = 200 },
                ],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        Assert.Equal([0, 1], rows.Select(r => r.CarIdx));
    }

    private static SyntheticMemoryBuilder WeekendVars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("SessionNum", IrsdkVarType.Int);
        builder.AddVar("CarIdxPosition", IrsdkVarType.Int, count: 4);
        builder.AddVar("CarIdxLap", IrsdkVarType.Int, count: 4);
        builder.AddVar("CarIdxEstTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxLastLapTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxBestLapTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxOnPitRoad", IrsdkVarType.Bool, count: 4);
        return builder;
    }

    /// <summary>A normal race weekend: Practice is session 0, Qualify 1, Race 2.</summary>
    private static IracingSessionInfo Weekend() => new()
    {
        SessionInfo = new SessionInfoSection
        {
            Sessions =
            [
                new SessionEntry { SessionNum = 0, SessionType = "Open Practice", SessionName = "PRACTICE" },
                new SessionEntry { SessionNum = 1, SessionType = "Open Qualify", SessionName = "QUALIFY" },
                new SessionEntry { SessionNum = 2, SessionType = "Race", SessionName = "RACE" },
            ],
        },
        DriverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                new DriverEntry { CarIdx = 1, UserName = "Leader", CarNumber = "1" },
                new DriverEntry { CarIdx = 2, UserName = "Second", CarNumber = "2" },
                new DriverEntry { CarIdx = 3, UserName = "InGarage", CarNumber = "3" },
            ],
        },
    };

    [Fact]
    public void BuildStandings_InTheRaceOfAWeekendThatOpensWithPractice_UsesRaceOrderNotFastestLap()
    {
        // Regression test for "no BEST for anyone and the list is just in lobby order". The current
        // session used to be read from SessionInfo.CurrentSessionNum, a field iRacing never writes —
        // its own SDK docs state SessionInfo has a single child parameter, Sessions — so it stayed 0
        // and every lookup landed on session 0, Practice. The race therefore ran the qualifying-style
        // fastest-lap ranking, and since CarIdxBestLapTime resets per session nobody had a time yet,
        // which collapsed the whole thing to roster order.
        var snapshot = TestSnapshotFactory.Build(WeekendVars(), w =>
        {
            w.SetInt("SessionNum", 2); // the race
            w.SetIntArray("CarIdxPosition", [3, 1, 2, 0]);
            w.SetIntArray("CarIdxLap", [2, 3, 3, -1]); // car 3 never left the garage
            w.SetFloatArray("CarIdxEstTime", [40.0f, 10.0f, 5.0f, 0f]);
            w.SetFloatArray("CarIdxLastLapTime", [-1f, -1f, -1f, -1f]);
            w.SetFloatArray("CarIdxBestLapTime", [-1f, -1f, -1f, -1f]);
        });

        var rows = StandingsBuilder.BuildStandings(snapshot, Weekend(), new SessionBestLapTracker());

        Assert.Equal([1, 2, 0], rows.Select(r => r.CarIdx));
        Assert.DoesNotContain(rows, r => r.CarIdx == 3);
    }

    [Fact]
    public void BuildStandings_InTheRace_ShowsBestLapStraightFromTelemetry()
    {
        var snapshot = TestSnapshotFactory.Build(WeekendVars(), w =>
        {
            w.SetInt("SessionNum", 2);
            w.SetIntArray("CarIdxPosition", [3, 1, 2, 0]);
            w.SetIntArray("CarIdxLap", [8, 9, 9, -1]);
            w.SetFloatArray("CarIdxEstTime", [40.0f, 10.0f, 5.0f, 0f]);
            w.SetFloatArray("CarIdxLastLapTime", [93.5f, 91.2f, 92.0f, -1f]);
            w.SetFloatArray("CarIdxBestLapTime", [93.0f, 90.4f, 91.5f, -1f]);
        });

        var leader = StandingsBuilder.BuildStandings(snapshot, Weekend(), new SessionBestLapTracker())
            .Single(r => r.CarIdx == 1);

        Assert.Equal(90.4, leader.BestLapTime, precision: 2);
        Assert.True(leader.IsSessionFastestLap);
    }

    [Theory]
    [InlineData(0)] // Practice
    [InlineData(1)] // Qualify
    public void BuildStandings_InPracticeOrQualifyingOfTheSameWeekend_StillRanksByFastestLap(int sessionNum)
    {
        var snapshot = TestSnapshotFactory.Build(WeekendVars(), w =>
        {
            w.SetInt("SessionNum", sessionNum);
            w.SetIntArray("CarIdxLap", [5, 6, 7, 0]);
            w.SetFloatArray("CarIdxEstTime", [40.0f, 10.0f, 5.0f, 0f]);
            w.SetFloatArray("CarIdxLastLapTime", [93.5f, 91.2f, 92.0f, -1f]);
            w.SetFloatArray("CarIdxBestLapTime", [93.0f, 90.4f, 91.5f, -1f]);
        });

        var rows = StandingsBuilder.BuildStandings(snapshot, Weekend(), new SessionBestLapTracker());

        // Fastest first, and the driver with no time at all sorts last but still appears.
        Assert.Equal([1, 2, 0, 3], rows.Select(r => r.CarIdx));
    }

    /// <summary>The session's scoring table as iRacing publishes it, carrying times set before the
    /// overlay ever connected.</summary>
    private static List<SessionResultPosition> ScoringTable() =>
    [
        new SessionResultPosition { CarIdx = 1, Position = 1, ClassPosition = 1, LapsComplete = 9, FastestTime = 90.4, LastTime = 91.2 },
        new SessionResultPosition { CarIdx = 2, Position = 2, ClassPosition = 2, LapsComplete = 8, FastestTime = 91.5, LastTime = 92.0 },
        new SessionResultPosition { CarIdx = 0, Position = 3, ClassPosition = 3, LapsComplete = 7, FastestTime = 93.0, LastTime = 93.5 },
        new SessionResultPosition { CarIdx = 3, Position = 4, ClassPosition = 4, LapsComplete = 0, FastestTime = -1, LastTime = -1 },
    ];

    private static IracingSessionInfo SessionWithScoring(string sessionType, List<SessionResultPosition> results) => new()
    {
        SessionInfo = new SessionInfoSection
        {
            Sessions = [new SessionEntry { SessionNum = 0, SessionType = sessionType, ResultsPositions = results }],
        },
        DriverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                new DriverEntry { CarIdx = 1, UserName = "Fast", CarNumber = "1" },
                new DriverEntry { CarIdx = 2, UserName = "Mid", CarNumber = "2" },
                new DriverEntry { CarIdx = 3, UserName = "NoTime", CarNumber = "3" },
            ],
        },
    };

    /// <summary>The overlay attaching to a session already underway: this client has not witnessed
    /// anyone cross the line, so every CarIdx* lap-time entry is still iRacing's -1 sentinel.</summary>
    private static TelemetrySnapshot JustAttachedMidSession() =>
        TestSnapshotFactory.Build(WeekendVars(), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxPosition", [3, 1, 2, 0]);
            w.SetIntArray("CarIdxLap", [7, 9, 8, 0]);
            w.SetFloatArray("CarIdxEstTime", [10f, 20f, 30f, 0f]);
            w.SetFloatArray("CarIdxLastLapTime", [-1f, -1f, -1f, -1f]);
            w.SetFloatArray("CarIdxBestLapTime", [-1f, -1f, -1f, -1f]);
        });

    [Fact]
    public void BuildStandings_OpenedMidSession_TakesLapTimesFromTheScoringTable()
    {
        // Regression test for "I open the overlay and nobody has a BEST unless someone sets a new
        // lap". The CarIdx* lap-time arrays are event-driven — they only carry a time once this
        // client has seen that car cross the line — so on attach they are all -1 and reading them
        // alone showed an empty column. iRacing's own ResultsPositions table already holds the whole
        // session's history at that point.
        var rows = StandingsBuilder.BuildStandings(
            JustAttachedMidSession(), SessionWithScoring("Open Practice", ScoringTable()), new SessionBestLapTracker());

        Assert.Equal(90.4, rows.Single(r => r.CarIdx == 1).BestLapTime, precision: 2);
        Assert.Equal(91.2, rows.Single(r => r.CarIdx == 1).LastLapTime, precision: 2);
        Assert.True(rows.Single(r => r.CarIdx == 1).IsSessionFastestLap);
    }

    [Fact]
    public void BuildStandings_OpenedMidSession_RanksByScoredTimesInsteadOfRosterOrder()
    {
        var rows = StandingsBuilder.BuildStandings(
            JustAttachedMidSession(), SessionWithScoring("Open Practice", ScoringTable()), new SessionBestLapTracker());

        // Fastest first; the driver who never set a time still appears, at the bottom.
        Assert.Equal([1, 2, 0, 3], rows.Select(r => r.CarIdx));
        Assert.Equal("—", rows.Last().BestLapDisplay);
    }

    [Fact]
    public void BuildStandings_LiveLapBeatsTheScoredOne()
    {
        // Once the client does witness a lap, live telemetry is the fresher of the two.
        var snapshot = TestSnapshotFactory.Build(WeekendVars(), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxLap", [7, 9, 8, 0]);
            w.SetFloatArray("CarIdxEstTime", [10f, 20f, 30f, 0f]);
            w.SetFloatArray("CarIdxLastLapTime", [-1f, 89.0f, -1f, -1f]);
            w.SetFloatArray("CarIdxBestLapTime", [-1f, 89.0f, -1f, -1f]);
        });

        var rows = StandingsBuilder.BuildStandings(
            snapshot, SessionWithScoring("Open Practice", ScoringTable()), new SessionBestLapTracker());

        Assert.Equal(89.0, rows.Single(r => r.CarIdx == 1).BestLapTime, precision: 2);
    }

    [Fact]
    public void BuildStandings_NoScoringTableYet_StillWorksFromTelemetryAlone()
    {
        var snapshot = TestSnapshotFactory.Build(WeekendVars(), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxLap", [7, 9, 8, 0]);
            w.SetFloatArray("CarIdxEstTime", [10f, 20f, 30f, 0f]);
            w.SetFloatArray("CarIdxLastLapTime", [93.5f, 91.2f, 92.0f, -1f]);
            w.SetFloatArray("CarIdxBestLapTime", [93.0f, 90.4f, 91.5f, -1f]);
        });

        var rows = StandingsBuilder.BuildStandings(
            snapshot, SessionWithScoring("Open Practice", []), new SessionBestLapTracker());

        Assert.Equal([1, 2, 0, 3], rows.Select(r => r.CarIdx));
        Assert.Equal(90.4, rows.Single(r => r.CarIdx == 1).BestLapTime, precision: 2);
    }

    [Fact]
    public void BuildRelative_OpenedMidSession_KeepsCarsOnOtherLapNumbers()
    {
        // Same root cause as the standings BEST column, different symptom. Relative needs a
        // reference lap length to fold gaps into a half-lap window, and without one it drops every
        // car that isn't on the player's exact lap number — which in an open practice, where
        // everyone joined at a different time, is almost the whole field. On a mid-session attach
        // the telemetry lap times are all -1, so the length has to come from the scoring table.
        var builder = RelativeVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxLap", [8, 20, 12, 0]); // wildly different lap counts...
            w.SetFloatArray("CarIdxEstTime", [40.0f, 45.0f, 36.0f, 0f]); // ...but all close on track
            w.SetFloatArray("CarIdxLapDistPct", [0.44f, 0.5f, 0.4f, 0f]);
            w.SetFloatArray("CarIdxLastLapTime", [-1f, -1f, -1f, -1f]);
            w.SetFloatArray("CarIdxBestLapTime", [-1f, -1f, -1f, -1f]);
        });

        var session = new IracingSessionInfo
        {
            SessionInfo = new SessionInfoSection
            {
                Sessions =
                [
                    new SessionEntry
                    {
                        SessionNum = 0,
                        SessionType = "Open Practice",
                        ResultsPositions =
                        [
                            new SessionResultPosition { CarIdx = 0, LapsComplete = 8, FastestTime = 94.0, LastTime = 95.0 },
                            new SessionResultPosition { CarIdx = 1, LapsComplete = 20, FastestTime = 90.0, LastTime = 91.0 },
                            new SessionResultPosition { CarIdx = 2, LapsComplete = 12, FastestTime = 92.0, LastTime = 93.0 },
                        ],
                    },
                ],
            },
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Ahead", CarNumber = "1" },
                    new DriverEntry { CarIdx = 2, UserName = "Behind", CarNumber = "2" },
                ],
            },
        };

        var rows = RelativeRows(snapshot, session);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.InRange(r.GapSeconds, -10, 10));
    }

    /// <summary>Overall order with mixed classes, exactly the shape BuildStandings emits.</summary>
    private static List<StandingsRow> MixedField(params (int carIdx, int classId, string className, bool isPlayer)[] specs)
    {
        var classRank = new Dictionary<int, int>();
        var rows = new List<StandingsRow>();
        for (var i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            classRank.TryGetValue(spec.classId, out var rank);
            rank++;
            classRank[spec.classId] = rank;

            rows.Add(new StandingsRow
            {
                CarIdx = spec.carIdx,
                Position = i + 1,
                ClassPosition = rank,
                Name = $"Driver {spec.carIdx}",
                CarNumber = spec.carIdx.ToString(),
                IsPlayer = spec.isPlayer,
                OnPitRoad = false,
                CurrentLap = 10,
                GapToLeaderSeconds = i,
                LastLapTime = 0,
                BestLapTime = 0,
                IsMultiClass = true,
                IRating = 0,
                LicString = "",
                IRatingDelta = 0,
                IsSessionFastestLap = false,
                CarClassID = spec.classId,
                CarClassName = spec.className,
            });
        }

        return rows;
    }

    private static List<StandingsRow> ThreeClassField() => MixedField(
        (100, 1, "GTP", false), (101, 1, "GTP", false), (102, 1, "GTP", false), (103, 1, "GTP", false),
        (200, 2, "LMP2", false), (201, 2, "LMP2", false), (202, 2, "LMP2", false),
        (300, 3, "GT3", false), (301, 3, "GT3", false), (302, 3, "GT3", false), (303, 3, "GT3", false),
        (304, 3, "GT3", false), (305, 3, "GT3", false), (306, 3, "GT3", false), (307, 3, "GT3", true),
        (308, 3, "GT3", false), (309, 3, "GT3", false));

    [Fact]
    public void BuildMulticlassView_GivesEveryClassAHeaderAndItsOwnPodium()
    {
        var display = StandingsBuilder.BuildMulticlassView(ThreeClassField(), maxDynamicDrivers: 5);

        var headers = display.OfType<StandingsHeaderRow>().Select(h => h.ClassName).ToList();
        Assert.Equal(["GT3", "GTP", "LMP2"], headers); // player's class first, then by leader's overall position
        Assert.Equal([100, 101, 102], display.OfType<StandingsRow>().Where(r => r.CarClassID == 1).Select(r => r.CarIdx));
        Assert.Equal([200, 201, 202], display.OfType<StandingsRow>().Where(r => r.CarClassID == 2).Select(r => r.CarIdx));
    }

    [Fact]
    public void BuildMulticlassView_PlayerClassKeepsThePodiumAndTheBlockAroundThePlayer()
    {
        var display = StandingsBuilder.BuildMulticlassView(ThreeClassField(), maxDynamicDrivers: 5);

        // Class podium, then the window centred on the player — positions within the class, not overall.
        Assert.Equal(
            [300, 301, 302, 305, 306, 307, 308, 309],
            display.OfType<StandingsRow>().Where(r => r.CarClassID == 3).Select(r => r.CarIdx));
        Assert.Single(display.OfType<StandingsSeparatorRow>());
    }

    [Fact]
    public void BuildMulticlassView_NeverRepeatsADriver()
    {
        // The player sitting inside their own class podium is the case that would duplicate.
        var field = MixedField(
            (100, 1, "GTP", false), (101, 1, "GTP", false),
            (300, 3, "GT3", true), (301, 3, "GT3", false), (302, 3, "GT3", false),
            (303, 3, "GT3", false), (304, 3, "GT3", false), (305, 3, "GT3", false));

        var carIdxs = StandingsBuilder.BuildMulticlassView(field, maxDynamicDrivers: 3)
            .OfType<StandingsRow>().Select(r => r.CarIdx).ToList();

        Assert.Equal(carIdxs.Distinct(), carIdxs);
        Assert.Equal([300, 301, 302, 303, 304, 305], carIdxs.Where(i => i >= 300));
    }

    [Fact]
    public void BuildMulticlassView_ClassWithFewerDriversThanThePodium_ContributesOnlyWhatExists()
    {
        var field = MixedField((100, 1, "GTP", false), (300, 3, "GT3", true), (301, 3, "GT3", false));

        var display = StandingsBuilder.BuildMulticlassView(field, maxDynamicDrivers: 7);

        Assert.Equal([300, 301, 100], display.OfType<StandingsRow>().Select(r => r.CarIdx));
        Assert.Empty(display.OfType<StandingsSeparatorRow>());
    }

    [Fact]
    public void BuildMulticlassView_SingleClassSession_FallsThroughWithNoClassHeader()
    {
        var rows = MakeField(10, playerPosition: 6);

        var display = StandingsBuilder.BuildMulticlassView(rows, maxDynamicDrivers: 5);

        Assert.Empty(display.OfType<StandingsHeaderRow>());
        Assert.Equal(
            StandingsBuilder.BuildFocusedView(rows, 5).OfType<StandingsRow>().Select(r => r.CarIdx),
            display.OfType<StandingsRow>().Select(r => r.CarIdx));
    }

    [Fact]
    public void BuildMulticlassView_RowCountStaysConstantAsThePlayerMovesThroughTheirClass()
    {
        var counts = Enumerable.Range(0, 10)
            .Select(playerIdx => StandingsBuilder.BuildMulticlassView(
                MixedField(Enumerable.Range(0, 10)
                    .Select(i => (carIdx: 300 + i, classId: 3, className: "GT3", isPlayer: i == playerIdx))
                    .Concat(Enumerable.Range(0, 4).Select(i => (carIdx: 100 + i, classId: 1, className: "GTP", isPlayer: false)))
                    .ToArray()),
                maxDynamicDrivers: 5).Count)
            .Distinct();

        Assert.Single(counts);
    }

    [Fact]
    public void BuildFocusedView_MulticlassSession_PinsThePlayersClassPodiumNotTheOverallOne()
    {
        // With the multiclass layout switched off the widget is a single block, and that block is
        // the player's own class: the overall podium belongs to whichever category is quickest, a
        // race the player can never be classified against.
        var display = StandingsBuilder.BuildFocusedView(ThreeClassField(), maxDynamicDrivers: 5);

        var rows = display.OfType<StandingsRow>().ToList();
        Assert.All(rows, r => Assert.Equal(3, r.CarClassID));
        Assert.Equal([300, 301, 302], rows.Take(3).Select(r => r.CarIdx));
        Assert.Contains(rows, r => r.IsPlayer);
        Assert.Empty(display.OfType<StandingsHeaderRow>());
    }

    [Fact]
    public void BuildFocusedView_MulticlassSession_CountsOnlyOwnClassCarsAsSkipped()
    {
        var separator = StandingsBuilder.BuildFocusedView(ThreeClassField(), maxDynamicDrivers: 5)
            .OfType<StandingsSeparatorRow>().Single();

        // GT3 P4 and P5 are the only cars between the class podium and the block — cars from the
        // other classes are not part of this list at all.
        Assert.Equal(2, separator.SkippedCount);
    }

    [Fact]
    public void BuildFocusedView_SpectatingMulticlass_FallsBackToTheLeadingClass()
    {
        var field = MixedField(
            (100, 1, "GTP", false), (101, 1, "GTP", false), (102, 1, "GTP", false), (103, 1, "GTP", false),
            (300, 3, "GT3", false), (301, 3, "GT3", false));

        var rows = StandingsBuilder.BuildFocusedView(field, maxDynamicDrivers: 3)
            .OfType<StandingsRow>().ToList();

        Assert.All(rows, r => Assert.Equal(1, r.CarClassID));
        Assert.Equal(4, rows.Count);
    }

    private static IracingSessionInfo TwoClassSession(string sessionType) => new()
    {
        SessionInfo = new SessionInfoSection { Sessions = [new SessionEntry { SessionNum = 0, SessionType = sessionType }] },
        DriverInfo = new DriverInfoSection
        {
            DriverCarIdx = 3,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "GTP Leader", CarNumber = "1", CarClassID = 1, CarClassShortName = "GTP" },
                new DriverEntry { CarIdx = 1, UserName = "GTP Second", CarNumber = "2", CarClassID = 1, CarClassShortName = "GTP" },
                new DriverEntry { CarIdx = 2, UserName = "GT3 Leader", CarNumber = "3", CarClassID = 3, CarClassShortName = "GT3" },
                new DriverEntry { CarIdx = 3, UserName = "Me", CarNumber = "4", CarClassID = 3, CarClassShortName = "GT3" },
                new DriverEntry { CarIdx = 4, UserName = "GT3 Third", CarNumber = "5", CarClassID = 3, CarClassShortName = "GT3" },
            ],
        },
    };

    [Fact]
    public void BuildStandings_Multiclass_GapIsMeasuredAgainstTheClassLeader()
    {
        // Every car on lap 10, spread down the track. Overall order is 0,1,2,3,4 — but a GT3 driver
        // being told they are 46s behind a prototype is a number they can do nothing with.
        var snapshot = TestSnapshotFactory.Build(WeekendVars(), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxLap", [10, 10, 10, 10]);
            w.SetFloatArray("CarIdxEstTime", [90f, 80f, 50f, 44f]);
            w.SetFloatArray("CarIdxLastLapTime", [95f, 95f, 100f, 100f]);
            w.SetFloatArray("CarIdxBestLapTime", [94f, 94f, 99f, 99f]);
        });

        var rows = StandingsBuilder.BuildStandings(snapshot, TwoClassSession("Race"), new SessionBestLapTracker());

        Assert.Equal("Leader", rows.Single(r => r.CarIdx == 0).GapDisplay); // GTP leader
        Assert.Equal("Leader", rows.Single(r => r.CarIdx == 2).GapDisplay); // GT3 leader
        Assert.Equal("+10.0", rows.Single(r => r.CarIdx == 1).GapDisplay); // behind the GTP leader
        Assert.Equal("+6.0", rows.Single(r => r.CarIdx == 3).GapDisplay);  // behind the GT3 leader
    }

    [Fact]
    public void BuildStandings_MulticlassQualifying_GapIsMeasuredAgainstTheClassPole()
    {
        var snapshot = TestSnapshotFactory.Build(WeekendVars(), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxLap", [10, 10, 10, 10]);
            w.SetFloatArray("CarIdxEstTime", [90f, 80f, 50f, 44f]);
            w.SetFloatArray("CarIdxLastLapTime", [90f, 91f, 100f, 101.5f]);
            w.SetFloatArray("CarIdxBestLapTime", [90f, 91f, 100f, 101.5f]);
        });

        var rows = StandingsBuilder.BuildStandings(snapshot, TwoClassSession("Open Qualify"), new SessionBestLapTracker());

        Assert.Equal("Leader", rows.Single(r => r.CarIdx == 2).GapDisplay); // GT3 pole
        Assert.Equal("+1.5", rows.Single(r => r.CarIdx == 3).GapDisplay);
        Assert.Equal("+1.0", rows.Single(r => r.CarIdx == 1).GapDisplay);
    }

    [Fact]
    public void BuildStandings_CarNotOnTrack_ShowsScoredLapsInsteadOfIracingsMinusOneSentinel()
    {
        // CarIdxLap is -1 for any car that isn't on track — in the garage, or back in the pit stall
        // after a run — which says nothing about how many laps it ran. Car 1 did 10 laps and parked;
        // car 3 has genuinely never been out.
        var snapshot = TestSnapshotFactory.Build(WeekendVars(), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxLap", [-1, -1, 4, -1]);
            w.SetFloatArray("CarIdxEstTime", [0f, 0f, 30f, 0f]);
            w.SetFloatArray("CarIdxLastLapTime", [-1f, -1f, 95f, -1f]);
            w.SetFloatArray("CarIdxBestLapTime", [-1f, -1f, 94f, -1f]);
        });

        var session = SessionWithScoring("Open Practice",
        [
            new SessionResultPosition { CarIdx = 1, Position = 1, LapsComplete = 10, FastestTime = 92.0, LastTime = 93.0 },
            new SessionResultPosition { CarIdx = 2, Position = 2, LapsComplete = 3, FastestTime = 94.0, LastTime = 95.0 },
        ]);

        var rows = StandingsBuilder.BuildStandings(snapshot, session, new SessionBestLapTracker());

        Assert.Equal("10", rows.Single(r => r.CarIdx == 1).LapDisplay); // parked, but scored
        Assert.Equal("4", rows.Single(r => r.CarIdx == 2).LapDisplay);  // live value wins
        Assert.Equal("—", rows.Single(r => r.CarIdx == 3).LapDisplay);  // never out
        Assert.All(rows, r => Assert.DoesNotContain("-", r.LapDisplay));
    }

    [Fact]
    public void BuildStandings_OnTheGrid_KeepsLapZeroAsARealValue()
    {
        // Zero is a genuine lap count before the start and must not be turned into a dash.
        var snapshot = TestSnapshotFactory.Build(WeekendVars(), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxPosition", [1, 2, 3, 0]);
            w.SetIntArray("CarIdxLap", [0, 0, 0, -1]);
            w.SetFloatArray("CarIdxEstTime", [0f, 0f, 0f, 0f]);
        });

        var rows = StandingsBuilder.BuildStandings(snapshot, SessionWithScoring("Race", []), new SessionBestLapTracker());

        Assert.All(rows, r => Assert.Equal("0", r.LapDisplay));
        Assert.DoesNotContain(rows, r => r.CarIdx == 3);
    }

    private static IracingSessionInfo RosterOf(int playerCarIdx, params DriverEntry[] drivers) => new()
    {
        DriverInfo = new DriverInfoSection { DriverCarIdx = playerCarIdx, Drivers = drivers.ToList() },
    };

    [Fact]
    public void SingleClassCarName_SingleClassSession_NamesThePlayersCar()
    {
        var session = RosterOf(2,
            new DriverEntry { CarIdx = 0, UserName = "A", CarClassID = 100, CarScreenNameShort = "911 GT3 Cup" },
            new DriverEntry { CarIdx = 1, UserName = "B", CarClassID = 100, CarScreenNameShort = "911 GT3 Cup" },
            new DriverEntry { CarIdx = 2, UserName = "Me", CarClassID = 100, CarScreenNameShort = "911 GT3 Cup" });

        Assert.Equal("911 GT3 Cup", StandingsBuilder.SingleClassCarName(session));
    }

    [Fact]
    public void SingleClassCarName_Multiclass_IsEmptyBecauseClassHeadersAlreadySayIt()
    {
        var session = RosterOf(0,
            new DriverEntry { CarIdx = 0, UserName = "Me", CarClassID = 100, CarScreenNameShort = "911 GT3 R", CarClassShortName = "GT3" },
            new DriverEntry { CarIdx = 1, UserName = "Proto", CarClassID = 200, CarScreenNameShort = "Dallara P217", CarClassShortName = "LMP2" });

        Assert.Equal("", StandingsBuilder.SingleClassCarName(session));
    }

    [Fact]
    public void SingleClassCarName_PaceCarDoesNotCountAsASecondClass()
    {
        var session = RosterOf(0,
            new DriverEntry { CarIdx = 0, UserName = "Me", CarClassID = 100, CarScreenNameShort = "MX-5" },
            new DriverEntry { CarIdx = 1, UserName = "Pace", CarClassID = 999, CarScreenNameShort = "Pace Car", CarIsPaceCar = 1 });

        Assert.Equal("MX-5", StandingsBuilder.SingleClassCarName(session));
    }

    [Fact]
    public void SingleClassCarName_BlankCarName_FallsBackToTheClassName()
    {
        var session = RosterOf(0,
            new DriverEntry { CarIdx = 0, UserName = "Me", CarClassID = 100, CarScreenNameShort = "", CarClassShortName = "GT4" });

        Assert.Equal("GT4", StandingsBuilder.SingleClassCarName(session));
    }

    [Fact]
    public void SingleClassCarName_NoSessionOrEmptyRoster_IsEmpty()
    {
        Assert.Equal("", StandingsBuilder.SingleClassCarName(null));
        Assert.Equal("", StandingsBuilder.SingleClassCarName(RosterOf(0)));
    }

    private static SyntheticMemoryBuilder FieldVars(int cars)
    {
        var b = new SyntheticMemoryBuilder();
        b.AddVar("SessionNum", IrsdkVarType.Int);
        b.AddVar("CarIdxPosition", IrsdkVarType.Int, count: cars);
        b.AddVar("CarIdxLap", IrsdkVarType.Int, count: cars);
        b.AddVar("CarIdxLapDistPct", IrsdkVarType.Float, count: cars);
        b.AddVar("CarIdxEstTime", IrsdkVarType.Float, count: cars);
        b.AddVar("CarIdxLastLapTime", IrsdkVarType.Float, count: cars);
        b.AddVar("CarIdxBestLapTime", IrsdkVarType.Float, count: cars);
        b.AddVar("CarIdxOnPitRoad", IrsdkVarType.Bool, count: cars);
        return b;
    }

    private static IracingSessionInfo FieldOf(int cars, int playerCarIdx) => new()
    {
        SessionInfo = new SessionInfoSection { Sessions = [new SessionEntry { SessionNum = 0, SessionType = "Race" }] },
        DriverInfo = new DriverInfoSection
        {
            DriverCarIdx = playerCarIdx,
            Drivers = Enumerable.Range(0, cars).Select(i => new DriverEntry
            {
                CarIdx = i,
                UserName = $"Driver {i}",
                CarNumber = i.ToString(),
                CarClassID = 100,
                IRating = 2000 + i,
                LicString = "A 3.50",
            }).ToList(),
        },
    };

    /// <summary>Nine cars strung out along the track, all on the same lap.</summary>
    private static TelemetrySnapshot NineCarsOnTrack() =>
        TestSnapshotFactory.Build(FieldVars(9), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxPosition", [1, 2, 3, 4, 5, 6, 7, 8, 9]);
            w.SetIntArray("CarIdxLap", [10, 10, 10, 10, 10, 10, 10, 10, 10]);
            w.SetFloatArray("CarIdxEstTime", [80f, 70f, 60f, 50f, 40f, 30f, 20f, 10f, 5f]);
            w.SetFloatArray("CarIdxLastLapTime", [95f, 95f, 95f, 95f, 95f, 95f, 95f, 95f, 95f]);
            w.SetFloatArray("CarIdxBestLapTime", [94f, 93f, 92f, 91f, 90f, 94f, 94f, 94f, 94f]);
        });

    [Theory]
    [InlineData(2, 5)]
    [InlineData(3, 7)]
    [InlineData(4, 9)]
    public void BuildRelative_ShowsTheConfiguredNumberOfDriversEachSide(int eachSide, int expectedRows)
    {
        var rows = StandingsBuilder.BuildRelative(NineCarsOnTrack(), FieldOf(9, 4), eachSide);

        Assert.Equal(expectedRows, rows.Count);
        Assert.True(rows.OfType<RelativeRow>().ToList()[eachSide].IsPlayer);
    }

    [Fact]
    public void BuildRelative_RowCountStaysConstantWhereverThePlayerRuns()
    {
        // The whole point of reserving slots: the widget must not resize mid-race as cars drift in
        // and out of the window.
        var counts = Enumerable.Range(0, 9)
            .Select(p => StandingsBuilder.BuildRelative(NineCarsOnTrack(), FieldOf(9, p), maxEachSide: 3).Count)
            .Distinct();

        Assert.Single(counts);
    }

    [Fact]
    public void BuildRelative_MostOfTheFieldInTheGarage_ReservesTheRemainingSlots()
    {
        var snapshot = TestSnapshotFactory.Build(FieldVars(9), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxLap", [10, 10, 10, -1, -1, -1, -1, -1, -1]);
            w.SetFloatArray("CarIdxEstTime", [60f, 50f, 40f, 0f, 0f, 0f, 0f, 0f, 0f]);
            w.SetFloatArray("CarIdxLastLapTime", [95f, 95f, 95f, -1f, -1f, -1f, -1f, -1f, -1f]);
            w.SetFloatArray("CarIdxBestLapTime", [94f, 93f, 92f, -1f, -1f, -1f, -1f, -1f, -1f]);
        });

        var rows = StandingsBuilder.BuildRelative(snapshot, FieldOf(9, 1), maxEachSide: 4);

        Assert.Equal(9, rows.Count);
        Assert.Equal(3, rows.OfType<RelativeRow>().Count());
        Assert.Equal(6, rows.OfType<RelativePlaceholderRow>().Count());
    }

    [Fact]
    public void BuildRelative_SessionSmallerThanTheWindow_IsNotPaddedPastItsRoster()
    {
        // Asking for 10 each side in a two-car session must not produce a column of blanks.
        var snapshot = TestSnapshotFactory.Build(FieldVars(9), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxLap", [10, 10, -1, -1, -1, -1, -1, -1, -1]);
            w.SetFloatArray("CarIdxEstTime", [50f, 40f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);
            w.SetFloatArray("CarIdxLastLapTime", [95f, 95f, -1f, -1f, -1f, -1f, -1f, -1f, -1f]);
            w.SetFloatArray("CarIdxBestLapTime", [94f, 93f, -1f, -1f, -1f, -1f, -1f, -1f, -1f]);
        });

        var rows = StandingsBuilder.BuildRelative(snapshot, FieldOf(2, 0), maxEachSide: 10);

        Assert.Equal(2, rows.Count);
        Assert.Empty(rows.OfType<RelativePlaceholderRow>());
    }

    [Fact]
    public void BuildRelative_CarriesEveryColumnStandingsDoes()
    {
        var me = StandingsBuilder.BuildRelative(NineCarsOnTrack(), FieldOf(9, 4), maxEachSide: 2)
            .OfType<RelativeRow>().Single(r => r.IsPlayer);

        Assert.Equal(2004, me.IRating);
        Assert.Equal("A 3.50", me.LicStringDisplay);
        Assert.Equal("10", me.LapDisplay);
        Assert.Equal("1:30.000", me.BestLapDisplay);
        Assert.Equal("1:35.000", me.LastLapDisplay);
        Assert.Equal("DRIVER 4", me.NameDisplay);
        Assert.True(me.IsSessionFastestLap);
        Assert.Equal("—", me.GapDisplay); // the player's own gap to themselves
    }

    [Theory]
    [InlineData("Open Practice")]
    [InlineData("Race")]
    public void BuildRelative_TakesPositionFromTheStandingsOrder(string sessionType)
    {
        // Regression test for "POS reads 0 in Relative but is right in Standings". iRacing only
        // assigns CarIdxPosition in scored sessions — it stays at 0 right through practice — while
        // Standings computes its own running order. Relative now shares that order, which also
        // stops the two widgets ever disagreeing about the same driver.
        var snapshot = TestSnapshotFactory.Build(FieldVars(5), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxPosition", [0, 0, 0, 0, 0]); // unscored, exactly as practice reports
            w.SetIntArray("CarIdxLap", [10, 10, 10, 10, 10]);
            w.SetFloatArray("CarIdxEstTime", [80f, 60f, 40f, 20f, 10f]);
            w.SetFloatArray("CarIdxLastLapTime", [95f, 96f, 97f, 98f, 99f]);
            w.SetFloatArray("CarIdxBestLapTime", [90f, 91f, 92f, 93f, 94f]);
        });

        var session = FieldOf(5, playerCarIdx: 2);
        session.SessionInfo!.Sessions[0].SessionType = sessionType;

        var standings = StandingsBuilder.BuildStandings(snapshot, session, new SessionBestLapTracker());
        var relative = StandingsBuilder.BuildRelative(snapshot, session, maxEachSide: 4, standings)
            .OfType<RelativeRow>().ToList();

        Assert.DoesNotContain(relative, r => r.PositionDisplay == "0");
        foreach (var row in relative)
        {
            Assert.Equal(standings.Single(s => s.CarIdx == row.CarIdx).PositionDisplay, row.PositionDisplay);
        }
    }

    [Fact]
    public void BuildRelative_WithNoStandingsOrderYet_FallsBackToIracingsOwnPosition()
    {
        var snapshot = TestSnapshotFactory.Build(FieldVars(5), w =>
        {
            w.SetInt("SessionNum", 0);
            w.SetIntArray("CarIdxPosition", [3, 1, 2, 4, 5]);
            w.SetIntArray("CarIdxLap", [10, 10, 10, 10, 10]);
            w.SetFloatArray("CarIdxEstTime", [80f, 60f, 40f, 20f, 10f]);
            w.SetFloatArray("CarIdxLastLapTime", [95f, 96f, 97f, 98f, 99f]);
            w.SetFloatArray("CarIdxBestLapTime", [90f, 91f, 92f, 93f, 94f]);
        });

        var relative = StandingsBuilder.BuildRelative(snapshot, FieldOf(5, 2), maxEachSide: 4)
            .OfType<RelativeRow>().ToList();

        Assert.Equal("1", relative.Single(r => r.CarIdx == 1).PositionDisplay);
    }

    private static List<StandingsRow> MakeRows(bool isMultiClass, params (int carIdx, bool isPlayer, int classId, string className, int position)[] specs) =>
    specs.Select(s => new StandingsRow
        {
            CarIdx = s.carIdx,
            Position = s.position,
            ClassPosition = 1,
            Name = $"Driver{s.carIdx}",
            CarNumber = s.carIdx.ToString(),
            IsPlayer = s.isPlayer,
            OnPitRoad = false,
            CurrentLap = 1,
            GapToLeaderSeconds = 0,
            LastLapTime = 0,
            BestLapTime = 0,
            IsMultiClass = isMultiClass,
            IRating = 0,
            LicString = "",
            IRatingDelta = 0,
            IsSessionFastestLap = false,
            CarClassID = s.classId,
            CarClassName = s.className,
        }).ToList();

    [Fact]
    public void GroupForDisplay_SingleClass_PassesThroughUnchanged()
    {
        var rows = MakeRows(isMultiClass: false, (0, true, 100, "GT3", 1), (1, false, 100, "GT3", 2));

        var display = StandingsBuilder.GroupForDisplay(rows);

        Assert.Equal(2, display.Count);
        Assert.All(display, item => Assert.IsType<StandingsRow>(item));
    }

    [Fact]
    public void GroupForDisplay_MultiClass_PlayerClassShowsAllRows_OthersCappedAtLimit()
    {
        var rows = MakeRows(
            isMultiClass: true,
            (0, true, 100, "GT3", 1),
            (1, false, 100, "GT3", 3),
            (2, false, 100, "GT3", 5),
            (3, false, 100, "GT3", 7),
            (4, false, 100, "GT3", 9),
            (5, false, 100, "GT3", 11),
            (6, false, 100, "GT3", 13), // player's class: 7 cars total, none capped
            (7, false, 200, "LMP2", 2),
            (8, false, 200, "LMP2", 4),
            (9, false, 200, "LMP2", 6),
            (10, false, 200, "LMP2", 8),
            (11, false, 200, "LMP2", 10),
            (12, false, 200, "LMP2", 12) // other class: 6 cars, should cap to 5
        );

        var display = StandingsBuilder.GroupForDisplay(rows, otherClassLimit: 5);

        var gt3Rows = display.OfType<StandingsRow>().Where(r => r.CarClassID == 100).ToList();
        var lmp2Rows = display.OfType<StandingsRow>().Where(r => r.CarClassID == 200).ToList();
        Assert.Equal(7, gt3Rows.Count);
        Assert.Equal(5, lmp2Rows.Count);

        var headers = display.OfType<StandingsHeaderRow>().ToList();
        Assert.Equal(2, headers.Count);
        Assert.Equal("GT3", headers[0].ClassName);
        Assert.Equal("LMP2", headers[1].ClassName);
    }

    [Fact]
    public void GroupForDisplay_PlayerClassBlockComesFirst_RegardlessOfOverallPosition()
    {
        // Player is in the slower class (running further back overall) — their class block should
        // still be the first thing shown, ahead of the other class that's actually leading the race.
        var rows = MakeRows(
            isMultiClass: true,
            (0, false, 200, "LMP2", 1),
            (1, true, 100, "GT3", 2),
            (2, false, 100, "GT3", 4)
        );

        var display = StandingsBuilder.GroupForDisplay(rows);

        var firstHeader = display.OfType<StandingsHeaderRow>().First();
        Assert.Equal("GT3", firstHeader.ClassName);
    }

    private static List<StandingsRow> MakeField(int size, int playerPosition) =>
        MakeRows(
            isMultiClass: false,
            Enumerable.Range(1, size)
                .Select(pos => (carIdx: pos, isPlayer: pos == playerPosition, classId: 100, className: "GT3", position: pos))
                .ToArray());

    [Fact]
    public void BuildFocusedView_MidfieldPlayer_PinsTopThreeAndCentresTheBlockOnThePlayer()
    {
        var display = StandingsBuilder.BuildFocusedView(MakeField(30, playerPosition: 15), maxDynamicDrivers: 5);

        // Top 3, a separator, then P13..P17 — the player plus two cars each side.
        Assert.Equal([1, 2, 3], display.OfType<StandingsRow>().Take(3).Select(r => r.Position));
        Assert.Single(display.OfType<StandingsSeparatorRow>());
        Assert.Equal([13, 14, 15, 16, 17], display.OfType<StandingsRow>().Skip(3).Select(r => r.Position));
    }

    [Fact]
    public void BuildFocusedView_SeparatorReportsHowManyCarsAreSkipped()
    {
        var display = StandingsBuilder.BuildFocusedView(MakeField(30, playerPosition: 15), maxDynamicDrivers: 5);

        // P4..P12 fall in the gap between the podium and the block starting at P13.
        Assert.Equal(9, display.OfType<StandingsSeparatorRow>().Single().SkippedCount);
    }

    [Fact]
    public void BuildFocusedView_PlayerInsideTopThree_ShowsNoDriverTwice()
    {
        var display = StandingsBuilder.BuildFocusedView(MakeField(30, playerPosition: 2), maxDynamicDrivers: 5);

        var positions = display.OfType<StandingsRow>().Select(r => r.Position).ToList();
        Assert.Equal(positions.Distinct(), positions);
        // The block can't reach above P4, so it simply continues from there.
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], positions);
    }

    [Fact]
    public void BuildFocusedView_PlayerLast_SlidesTheBlockBackInsteadOfShrinkingIt()
    {
        var display = StandingsBuilder.BuildFocusedView(MakeField(30, playerPosition: 30), maxDynamicDrivers: 5);

        // Same row count as any other position — the widget must not change height just because the
        // window ran out of field behind the player.
        Assert.Equal([26, 27, 28, 29, 30], display.OfType<StandingsRow>().Skip(3).Select(r => r.Position));
    }

    [Fact]
    public void BuildFocusedView_FieldSmallerThanTheBlock_ListsEveryoneWithNothingPadded()
    {
        var display = StandingsBuilder.BuildFocusedView(MakeField(6, playerPosition: 5), maxDynamicDrivers: 9);

        Assert.Equal([1, 2, 3, 4, 5, 6], display.OfType<StandingsRow>().Select(r => r.Position));
        Assert.Equal(0, display.OfType<StandingsSeparatorRow>().Single().SkippedCount);
    }

    [Fact]
    public void BuildFocusedView_SingleDriverSession_IsExactlyOneRow()
    {
        var display = StandingsBuilder.BuildFocusedView(MakeField(1, playerPosition: 1), maxDynamicDrivers: 7);

        Assert.Equal(1, display.OfType<StandingsRow>().Single().Position);
        Assert.Empty(display.OfType<StandingsSeparatorRow>());
    }

    [Fact]
    public void BuildFocusedView_EmptyField_ReturnsNothing()
    {
        Assert.Empty(StandingsBuilder.BuildFocusedView([], maxDynamicDrivers: 7));
    }

    [Fact]
    public void BuildFocusedView_Spectating_FallsBackToTheCarsBehindThePodium()
    {
        // Nobody flagged as the player: still a usable view rather than an empty dynamic block.
        var display = StandingsBuilder.BuildFocusedView(MakeField(30, playerPosition: -1), maxDynamicDrivers: 4);

        Assert.Equal([4, 5, 6, 7], display.OfType<StandingsRow>().Skip(3).Select(r => r.Position));
    }

    [Fact]
    public void BuildFocusedView_RowCountStaysConstantAcrossPlayerPositions()
    {
        // The whole point of sliding rather than shrinking the window: no size change as the player
        // moves up and down the order mid-race.
        var counts = new[] { 1, 4, 15, 29, 30 }
            .Select(pos => StandingsBuilder.BuildFocusedView(MakeField(30, pos), maxDynamicDrivers: 7).Count)
            .Distinct();

        Assert.Single(counts);
    }

    [Fact]
    public void ComputeStrengthOfField_UniformField_EqualsThatSharedIRating()
    {
        // Self-consistency check baked into iRacing's own published SOF formula: a field where
        // every driver carries the exact same iRating must produce that same number as the SOF.
        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Driver0", CarNumber = "0", IRating = 2500 },
                    new DriverEntry { CarIdx = 1, UserName = "Driver1", CarNumber = "1", IRating = 2500 },
                    new DriverEntry { CarIdx = 2, UserName = "Driver2", CarNumber = "2", IRating = 2500 },
                ],
            },
        };

        var sof = StandingsBuilder.ComputeStrengthOfField(session);

        Assert.Equal(2500, sof, precision: 3);
    }

    [Fact]
    public void ComputeStrengthOfField_NoRatedDrivers_ReturnsZero()
    {
        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers = [new DriverEntry { CarIdx = 0, UserName = "Driver0", CarNumber = "0" }],
            },
        };

        var sof = StandingsBuilder.ComputeStrengthOfField(session);

        Assert.Equal(0, sof);
    }

    [Fact]
    public void BuildStandings_HigherIRatingDriverFinishingAhead_GainsIRating()
    {
        // A lower-rated driver beating a field of higher-rated drivers should show a positive
        // delta; those higher-rated drivers they beat should show negative deltas.
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [1, 2, 3, 0]);
            w.SetIntArray("CarIdxLap", [5, 5, 5, 0]);
            w.SetFloatArray("CarIdxEstTime", [30.0f, 20.0f, 10.0f, 0]);
            w.SetFloatArray("CarIdxLastLapTime", [90f, 90f, 90f, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Underdog", CarNumber = "7", IRating = 1500 },
                    new DriverEntry { CarIdx = 1, UserName = "Mid", CarNumber = "8", IRating = 3000 },
                    new DriverEntry { CarIdx = 2, UserName = "Top", CarNumber = "9", IRating = 4500 },
                ],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        var underdog = rows.Single(r => r.CarIdx == 0);
        var top = rows.Single(r => r.CarIdx == 2);
        Assert.Equal(1, underdog.Position); // furthest along -> leading
        Assert.True(underdog.IRatingDelta > 0, "lower-rated driver leading a stronger field should gain");
        Assert.True(top.IRatingDelta < 0, "higher-rated driver finishing last should lose");
    }

    [Fact]
    public void BuildStandings_UnratedDrivers_GetZeroIRatingDelta()
    {
        var builder = StandingsVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetIntArray("CarIdxPosition", [1, 0, 0, 0]);
            w.SetIntArray("CarIdxLap", [5, 0, 0, 0]);
            w.SetFloatArray("CarIdxEstTime", [30.0f, 0, 0, 0]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers = [new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7", IRating = 0 }],
            },
        };

        var rows = StandingsBuilder.BuildStandings(snapshot, session);

        Assert.Equal(0, Assert.Single(rows).IRatingDelta);
    }
}
