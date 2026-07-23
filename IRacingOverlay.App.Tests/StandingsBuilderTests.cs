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

        var rows = StandingsBuilder.BuildRelative(snapshot, session);

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

        var rows = StandingsBuilder.BuildRelative(snapshot, session);

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
        Assert.Equal("1 (1)", leader.PositionDisplay);
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
