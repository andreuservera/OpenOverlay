using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class CockpitBuilderTests
{
    private static IracingSessionInfo SessionWithShiftLights(double first = 5000, double shift = 7000, double blink = 7200, double last = 0) =>
        new()
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                DriverCarSLFirstRPM = first,
                DriverCarSLShiftRPM = shift,
                DriverCarSLBlinkRPM = blink,
                DriverCarSLLastRPM = last,
                Drivers = [new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" }],
            },
        };

    private static SyntheticMemoryBuilder GearAndShiftVars(out SyntheticMemoryBuilder builder)
    {
        builder = new SyntheticMemoryBuilder();
        builder.AddVar("Gear", IrsdkVarType.Int);
        builder.AddVar("RPM", IrsdkVarType.Float);
        return builder;
    }

    [Theory]
    [InlineData(-1, "R")]
    [InlineData(0, "N")]
    [InlineData(3, "3")]
    [InlineData(6, "6")]
    public void Build_GearText_MapsCorrectly(int gear, string expected)
    {
        GearAndShiftVars(out var builder);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Gear", gear);
            w.SetFloat("RPM", 3000);
        });

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.Equal(expected, state.Gear);
    }

    [Fact]
    public void Build_RpmBelowFirstThreshold_NoShiftLightsLit()
    {
        GearAndShiftVars(out var builder);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Gear", 3);
            w.SetFloat("RPM", 4000); // below First=5000
        });

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.Equal(0, state.ShiftLightsLit);
        Assert.False(state.ShiftBlink);
    }

    [Fact]
    public void Build_RpmAtShiftPoint_AllShiftLightsLit()
    {
        GearAndShiftVars(out var builder);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Gear", 3);
            w.SetFloat("RPM", 7000); // == Shift
        });

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.Equal(CockpitState.ShiftLightCount, state.ShiftLightsLit);
    }

    [Fact]
    public void Build_RpmJustBelowShiftPoint_DoesNotBlink()
    {
        GearAndShiftVars(out var builder);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Gear", 3);
            w.SetFloat("RPM", 6950);
        });

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.False(state.ShiftBlink);
    }

    [Theory]
    [InlineData(7000)] // at the shift point, below the car's own blink RPM
    [InlineData(7300)] // past it
    public void Build_AtOrAboveShiftPoint_Blinks(float rpm)
    {
        GearAndShiftVars(out var builder);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Gear", 3);
            w.SetFloat("RPM", rpm);
        });

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.True(state.ShiftBlink);
    }

    [Fact]
    public void Build_AbsActive_PassesThroughFromTelemetry()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Gear", IrsdkVarType.Int);
        builder.AddVar("RPM", IrsdkVarType.Float);
        builder.AddVar("BrakeABSactive", IrsdkVarType.Bool);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetBool("BrakeABSactive", true);
        });

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.True(state.AbsActive);
    }

    [Fact]
    public void Build_AbsSetting_ReportsConfiguredLevelIndependentlyOfActivation()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("BrakeABSactive", IrsdkVarType.Bool);
        builder.AddVar("dcABS", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetBool("BrakeABSactive", false);
            w.SetFloat("dcABS", 4f);
        });

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.Equal(4, state.AbsLevel);
        Assert.False(state.AbsActive);
    }

    [Fact]
    public void Build_NoAbsSetting_LevelIsNull()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("BrakeABSactive", IrsdkVarType.Bool);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetBool("BrakeABSactive", true));

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.Null(state.AbsLevel);
    }

    [Fact]
    public void Build_Speed_ConvertsMetersPerSecondToKph()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Gear", IrsdkVarType.Int);
        builder.AddVar("RPM", IrsdkVarType.Float);
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 50f)); // 50 m/s -> 180 km/h

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.Equal(180.0, state.SpeedKph, precision: 3);
    }

    [Fact]
    public void Build_SpeedMissing_DefaultsToZero()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Gear", IrsdkVarType.Int);
        builder.AddVar("RPM", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetInt("Gear", 1));

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.Equal(0, state.SpeedKph);
    }

    [Fact]
    public void Build_Rpm_PassesThroughFromTelemetry()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Gear", IrsdkVarType.Int);
        builder.AddVar("RPM", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("RPM", 8650f));

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.Equal(8650, state.Rpm, precision: 3);
    }

    [Fact]
    public void Build_RpmMissing_DefaultsToZero()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Gear", IrsdkVarType.Int);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetInt("Gear", 1));

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights());

        Assert.Equal(0, state.Rpm);
    }

    private const double TrackMeters = 4000;

    private static SyntheticMemoryBuilder ProximityVars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("CarLeftRight", IrsdkVarType.Int); // confirmed Int live, despite what the docs say
        builder.AddVar("CarIdxLapDistPct", IrsdkVarType.Float, count: 4);
        builder.AddVar("CarIdxEstTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("Speed", IrsdkVarType.Float);
        return builder;
    }

    /// <summary>CarIdxLapDistPct of a car <paramref name="meters"/> along the track from mid-lap.</summary>
    private static float At(double meters) => (float)(0.5 + meters / TrackMeters);

    private static IracingSessionInfo TwoCarSession() => new()
    {
        WeekendInfo = new WeekendInfoSection { TrackLength = "4.0000 km" },
        DriverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                new DriverEntry { CarIdx = 1, UserName = "Alongside", CarNumber = "9" },
            ],
        },
    };

    // Every car has its own CarClassEstLapTime: GT3 (BoP) 114.60s, other-make GT3 115.97s, prototype 100s.
    private static IracingSessionInfo MulticlassSession() => new()
    {
        WeekendInfo = new WeekendInfoSection { TrackLength = "4.0000 km" },
        DriverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7", CarClassID = 100, CarID = 1 },
                new DriverEntry { CarIdx = 1, UserName = "GT3 Rival", CarNumber = "9", CarClassID = 100, CarID = 2 },
                new DriverEntry { CarIdx = 2, UserName = "Prototype", CarNumber = "1", CarClassID = 200, CarID = 3 },
                new DriverEntry { CarIdx = 3, UserName = "In Garage", CarNumber = "44", CarClassID = 100, CarID = 1 },
            ],
        },
    };

    [Fact]
    public void Build_CarLeftWithCloseGap_FillsLeftProximityOnly()
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 2); // irsdk_LRCarLeft
            w.SetFloatArray("CarIdxLapDistPct", [At(0), At(1), 0, 0]); // 1m gap, well inside a car length
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, TwoCarSession());

        Assert.True(state.LeftProximity.Amount > 0);
        Assert.Equal(0, state.RightProximity.Amount);
    }

    [Fact]
    public void Build_CarLeftRightClear_NoProximityEvenWithNearbyCar()
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 1); // irsdk_LRClear
            w.SetFloatArray("CarIdxLapDistPct", [At(0), At(1), 0, 0]); // 1m gap, but CarLeftRight says clear
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, TwoCarSession());

        Assert.Equal(0, state.LeftProximity.Amount);
        Assert.Equal(0, state.RightProximity.Amount);
    }

    [Fact]
    public void Build_CarRightButFarAway_ProximityClampedToZero()
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 3); // irsdk_LRCarRight
            w.SetFloatArray("CarIdxLapDistPct", [At(0), At(-250), 0, 0]); // 250m back — nowhere near alongside
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, TwoCarSession());

        Assert.Equal(0, state.RightProximity.Amount);
    }

    [Fact]
    public void Build_OvertakingCarBehindPullingClear_BandShrinksTowardBottom()
    {
        // Player is further along the track than the car alongside — its front
        // is behind ours, so the overlap should sit at the BOTTOM of our bar (near our rear), not
        // spread evenly, and should shrink toward the very bottom as the gap opens further.
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 2); // irsdk_LRCarLeft
            w.SetFloatArray("CarIdxLapDistPct", [At(1), At(0), 0, 0]); // player ahead by 1m, within a car length
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, TwoCarSession());

        Assert.True(state.LeftProximity.Amount > 0);
        Assert.True(state.LeftProximity.BandStart > 0, "overlap should start below our front, not at it");
        Assert.Equal(1, state.LeftProximity.BandEnd, precision: 5);
    }

    [Fact]
    public void Build_BeingOvertakenCarPullingAhead_BandShrinksTowardTop()
    {
        // Player is behind — the other car's front is ahead of ours, so the overlap
        // should sit at the TOP of our bar (near our front/nose), not the bottom.
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 3); // irsdk_LRCarRight
            w.SetFloatArray("CarIdxLapDistPct", [At(0), At(1), 0, 0]); // player behind by 1m
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, TwoCarSession());

        Assert.True(state.RightProximity.Amount > 0);
        Assert.Equal(0, state.RightProximity.BandStart, precision: 5);
        Assert.True(state.RightProximity.BandEnd < 1, "overlap should end above our rear, not at it");
    }

    [Fact]
    public void Build_DeadEvenAlongside_FullBand()
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 2);
            w.SetFloatArray("CarIdxLapDistPct", [At(0), At(0), 0, 0]); // dead even
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, TwoCarSession());

        Assert.Equal(1, state.LeftProximity.Amount, precision: 5);
        Assert.Equal(0, state.LeftProximity.BandStart, precision: 5);
        Assert.Equal(1, state.LeftProximity.BandEnd, precision: 5);
    }

    // Multiclass regressions: CarIdxEstTime below is what iRacing reports (position × each car's own
    // CarClassEstLapTime), which the old EstTime-difference check misread as tens to hundreds of metres.

    [Fact]
    public void Build_Multiclass_SameClassCarAlongside_Lights()
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 2); // irsdk_LRCarLeft
            w.SetFloatArray("CarIdxLapDistPct", [At(0), At(0), At(1000), -1]); // GT3 rival dead even
            w.SetFloatArray("CarIdxEstTime", [57.30f, 57.98f, 75.0f, 0]); // 0.68s "apart" = 34m at 50 m/s
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, MulticlassSession());

        Assert.Equal(1, state.LeftProximity.Amount, precision: 5);
        Assert.Equal(0, state.RightProximity.Amount);
    }

    [Fact]
    public void Build_Multiclass_FasterClassCarOvertaking_LightsAtOurFront()
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 3); // irsdk_LRCarRight
            w.SetFloatArray("CarIdxLapDistPct", [At(0), At(-300), At(2), -1]); // prototype's front 2m ahead of ours
            w.SetFloatArray("CarIdxEstTime", [57.30f, 49.29f, 50.05f, 0]); // prototype 7.25s "behind"
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, MulticlassSession());

        Assert.Equal(0, state.LeftProximity.Amount);
        Assert.Equal(0, state.RightProximity.BandStart, precision: 5);
        Assert.Equal((4.8 - 2) / 4.8, state.RightProximity.BandEnd, precision: 3);
    }

    [Fact]
    public void Build_CarAlongsideAcrossStartFinishLine_Lights()
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 2); // irsdk_LRCarLeft
            w.SetFloatArray("CarIdxLapDistPct", [0.0001f, 0.9999f, 0, 0]); // we've crossed the line, they haven't: 0.8m
            w.SetFloatArray("CarIdxEstTime", [0.01f, 114.59f, 0, 0]);
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, TwoCarSession());

        Assert.True(state.LeftProximity.Amount > 0);
        Assert.Equal(1, state.LeftProximity.BandEnd, precision: 5); // their front is behind ours
    }

    [Fact]
    public void Build_CarNotInWorld_IsNotMistakenForCarAlongside()
    {
        // Just past S/F, where a garage car's -1 position (or 0 EstTime) would read as right beside us.
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 2); // irsdk_LRCarLeft
            w.SetFloatArray("CarIdxLapDistPct", [0.0001f, 0.00085f, -1, -1]); // GT3 rival 3m ahead
            w.SetFloatArray("CarIdxEstTime", [0.0115f, 0.0986f, 0, 0]);
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, MulticlassSession());

        Assert.Equal(0, state.LeftProximity.BandStart, precision: 5);
        Assert.Equal((4.8 - 3) / 4.8, state.LeftProximity.BandEnd, precision: 3);
    }

    [Theory]
    [InlineData("4.0000 km", true)] // 0.001 of a lap = 4.0m: alongside
    [InlineData("5.7536 km", false)] // same fraction of a longer lap = 5.75m: clear
    [InlineData(null, false)] // unknown length: dark rather than a guess
    public void Build_GapIsMeasuredWithTheSessionTrackLength(string? trackLength, bool lit)
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 2); // irsdk_LRCarLeft
            w.SetFloatArray("CarIdxLapDistPct", [0.5f, 0.501f, 0, 0]);
            w.SetFloat("Speed", 50);
        });
        var session = TwoCarSession();
        session.WeekendInfo!.TrackLength = trackLength;

        var state = CockpitBuilder.Build(snapshot, session);

        Assert.Equal(lit, state.LeftProximity.Amount > 0);
    }

    // ===== Brake bias, traction control, incidents, delta =====

    private static TelemetrySnapshot CarSetupSnapshot(bool withAdjusters)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Gear", IrsdkVarType.Int);
        if (withAdjusters)
        {
            builder.AddVar("dcBrakeBias", IrsdkVarType.Float);
            builder.AddVar("dcTractionControl", IrsdkVarType.Float);
            builder.AddVar("PlayerCarMyIncidentCount", IrsdkVarType.Int);
            builder.AddVar("LapDeltaToSessionBestLap", IrsdkVarType.Float);
            builder.AddVar("LapDeltaToSessionBestLap_OK", IrsdkVarType.Bool);
        }

        return TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Gear", 3);
            if (withAdjusters)
            {
                w.SetFloat("dcBrakeBias", 54.5f);
                w.SetFloat("dcTractionControl", 3f);
                w.SetInt("PlayerCarMyIncidentCount", 9);
                w.SetFloat("LapDeltaToSessionBestLap", -0.25f);
                w.SetBool("LapDeltaToSessionBestLap_OK", true);
            }
        });
    }

    private static IracingSessionInfo SessionWithIncidentLimit(string limit) => new()
    {
        WeekendInfo = new WeekendInfoSection { WeekendOptions = new WeekendOptionsSection { IncidentLimit = limit } },
    };

    [Fact]
    public void Build_ReadsBrakeBiasTractionControlIncidentsAndDelta()
    {
        var state = CockpitBuilder.Build(CarSetupSnapshot(withAdjusters: true), SessionWithIncidentLimit("17"));

        Assert.Equal(54.5, state.BrakeBias!.Value, precision: 3);
        Assert.Equal(3, state.TractionControl);
        Assert.Equal(9, state.Incidents!.CountedTotal);
        Assert.Equal(17, state.Incidents.Limit);
        Assert.Equal(IncidentSeverity.Warning, state.Incidents.Severity);
        Assert.Equal(-0.25, state.Delta!.DeltaSeconds, precision: 3);
        Assert.True(state.Delta.IsValid);
        Assert.Empty(state.Unsupported);
    }

    [Fact]
    public void Build_TractionControlIsAWholeLevel()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("dcTractionControl", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("dcTractionControl", 2.9999f));

        Assert.Equal(3, CockpitBuilder.Build(snapshot, null).TractionControl);
    }

    [Fact]
    public void Build_DataTheCarDoesNotReport_IsNull_AndItsModulesUnsupported()
    {
        var state = CockpitBuilder.Build(CarSetupSnapshot(withAdjusters: false), null);

        Assert.Null(state.BrakeBias);
        Assert.Null(state.TractionControl);
        Assert.Null(state.Incidents);
        Assert.Null(state.Delta);
        Assert.Equal(
            new HashSet<CockpitModule> { CockpitModule.BrakeBias, CockpitModule.TractionControl, CockpitModule.Incidents, CockpitModule.Delta },
            state.Unsupported);
    }

    [Fact]
    public void Build_AnInvalidDelta_IsStillSupported()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("LapDeltaToSessionBestLap", IrsdkVarType.Float);
        builder.AddVar("LapDeltaToSessionBestLap_OK", IrsdkVarType.Bool);
        // No session-best lap yet: the module stays, showing a dash, rather than coming and going.
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetBool("LapDeltaToSessionBestLap_OK", false));

        var state = CockpitBuilder.Build(snapshot, null);

        Assert.False(state.Delta!.IsValid);
        Assert.DoesNotContain(CockpitModule.Delta, state.Unsupported);
    }

    [Fact]
    public void EmptyState_HidesNoModule()
    {
        Assert.Empty(CockpitState.Empty.Unsupported);
    }
}
