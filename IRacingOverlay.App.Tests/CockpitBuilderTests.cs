using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class CockpitBuilderTests
{
    private static IracingSessionInfo SessionWithShiftLights(double first = 5000, double shift = 7000, double blink = 7200) =>
        new()
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                DriverCarSLFirstRPM = first,
                DriverCarSLShiftRPM = shift,
                DriverCarSLBlinkRPM = blink,
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

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights(), new WheelSlipDetector());

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

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights(), new WheelSlipDetector());

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

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights(), new WheelSlipDetector());

        Assert.Equal(CockpitState.ShiftLightCount, state.ShiftLightsLit);
    }

    [Fact]
    public void Build_RpmAtOrAboveBlinkThreshold_Blinks()
    {
        GearAndShiftVars(out var builder);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("Gear", 3);
            w.SetFloat("RPM", 7300); // >= Blink=7200
        });

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights(), new WheelSlipDetector());

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

        var state = CockpitBuilder.Build(snapshot, SessionWithShiftLights(), new WheelSlipDetector());

        Assert.True(state.AbsActive);
    }

    private static SyntheticMemoryBuilder ProximityVars()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("CarLeftRight", IrsdkVarType.Int); // confirmed Int live, despite what the docs say
        builder.AddVar("CarIdxEstTime", IrsdkVarType.Float, count: 4);
        builder.AddVar("Speed", IrsdkVarType.Float);
        return builder;
    }

    private static IracingSessionInfo TwoCarSession() => new()
    {
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

    [Fact]
    public void Build_CarLeftWithCloseGap_FillsLeftProximityOnly()
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 2); // irsdk_LRCarLeft
            w.SetFloatArray("CarIdxEstTime", [10.0f, 10.02f, 0, 0]); // 0.02s gap
            w.SetFloat("Speed", 50); // m/s -> ~1m gap, well inside a car length
        });

        var state = CockpitBuilder.Build(snapshot, TwoCarSession(), new WheelSlipDetector());

        Assert.True(state.LeftProximity > 0);
        Assert.Equal(0, state.RightProximity);
    }

    [Fact]
    public void Build_CarLeftRightClear_NoProximityEvenWithNearbyCar()
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 1); // irsdk_LRClear
            w.SetFloatArray("CarIdxEstTime", [10.0f, 10.0f, 0, 0]);
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, TwoCarSession(), new WheelSlipDetector());

        Assert.Equal(0, state.LeftProximity);
        Assert.Equal(0, state.RightProximity);
    }

    [Fact]
    public void Build_CarRightButFarAway_ProximityClampedToZero()
    {
        var builder = ProximityVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetInt("CarLeftRight", 3); // irsdk_LRCarRight
            w.SetFloatArray("CarIdxEstTime", [10.0f, 5.0f, 0, 0]); // 5s gap — nowhere near alongside
            w.SetFloat("Speed", 50);
        });

        var state = CockpitBuilder.Build(snapshot, TwoCarSession(), new WheelSlipDetector());

        Assert.Equal(0, state.RightProximity);
    }
}
