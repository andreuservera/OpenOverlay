using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class EstTimeProfileTests
{
    private const double OurEstLap = 75.4;

    private static IracingSessionInfo Session(params DriverEntry[] others) => new()
    {
        DriverInfo = new DriverInfoSection
        {
            DriverCarIdx = 0,
            Drivers =
            [
                new DriverEntry { CarIdx = 0, UserName = "Me", CarID = 1, CarClassEstLapTime = OurEstLap },
                .. others,
            ],
        },
    };

    private static TelemetrySnapshot Tick(float[] pct, float[] estTime, bool[]? onPitRoad = null)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("CarIdxLapDistPct", IrsdkVarType.Float, count: 3);
        builder.AddVar("CarIdxEstTime", IrsdkVarType.Float, count: 3);
        builder.AddVar("CarIdxOnPitRoad", IrsdkVarType.Bool, count: 3);
        return TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloatArray("CarIdxLapDistPct", pct);
            w.SetFloatArray("CarIdxEstTime", estTime);
            w.SetBoolArray("CarIdxOnPitRoad", onPitRoad ?? [false, false, false]);
        });
    }

    [Fact]
    public void Interpolates_BetweenWhatOurCarHasDriven()
    {
        var profile = new EstTimeProfile();
        var session = Session();
        profile.Update(Tick([0.400f, -1, -1], [30.0f, 0, 0]), session);
        profile.Update(Tick([0.410f, -1, -1], [30.9f, 0, 0]), session);

        Assert.Equal(30.45, profile.EstTimeAt(0.405)!.Value, precision: 3);
    }

    [Fact]
    public void LearnsFromCarsOfOurModel_NotFromAnyOtherCar()
    {
        var profile = new EstTimeProfile();
        var session = Session(
            new DriverEntry { CarIdx = 1, CarID = 1, CarClassEstLapTime = OurEstLap },
            new DriverEntry { CarIdx = 2, CarID = 2, CarClassEstLapTime = 65.0 });

        profile.Update(Tick([0.10f, 0.600f, 0.300f], [7.5f, 45.0f, 19.5f]), session);
        profile.Update(Tick([0.11f, 0.605f, 0.305f], [8.3f, 45.4f, 19.8f]), session);

        Assert.Equal(45.2, profile.EstTimeAt(0.6025)!.Value, precision: 3);
        Assert.Null(profile.EstTimeAt(0.3025)); // another model's estimate is on another clock
    }

    [Fact]
    public void AStretchNotYetDriven_IsUnknown()
    {
        var profile = new EstTimeProfile();
        var session = Session();
        profile.Update(Tick([0.40f, -1, -1], [30.0f, 0, 0]), session);
        profile.Update(Tick([0.50f, -1, -1], [37.7f, 0, 0]), session);

        Assert.Null(profile.EstTimeAt(0.45)); // a tenth of a lap is far too long to draw a line across
        Assert.Null(profile.EstTimeAt(0.80));
    }

    [Fact]
    public void Interpolates_AcrossTheLine()
    {
        var profile = new EstTimeProfile();
        var session = Session();
        profile.Update(Tick([0.995f, -1, -1], [75.0f, 0, 0]), session);
        profile.Update(Tick([0.005f, -1, -1], [0.4f, 0, 0]), session);

        // The curve runs on from the end of one lap into the next: zero at the line itself.
        Assert.Equal(0.0, profile.EstTimeAt(0.0)!.Value, precision: 3);
        Assert.Equal(75.2, profile.EstTimeAt(0.9975)!.Value, precision: 3);
    }

    [Fact]
    public void IgnoresPitRoad()
    {
        var profile = new EstTimeProfile();
        var session = Session();
        profile.Update(Tick([0.40f, -1, -1], [30.0f, 0, 0], onPitRoad: [true, false, false]), session);
        profile.Update(Tick([0.41f, -1, -1], [30.9f, 0, 0], onPitRoad: [true, false, false]), session);

        Assert.Null(profile.EstTimeAt(0.405));
    }

    [Fact]
    public void ADifferentCar_StartsTheCurveOver()
    {
        var profile = new EstTimeProfile();
        profile.Update(Tick([0.400f, -1, -1], [30.0f, 0, 0]), Session());
        profile.Update(Tick([0.410f, -1, -1], [30.9f, 0, 0]), Session());

        var newCar = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers = [new DriverEntry { CarIdx = 0, CarID = 7, CarClassEstLapTime = 80.0 }],
            },
        };
        profile.Update(Tick([0.900f, -1, -1], [72.0f, 0, 0]), newCar);

        Assert.Null(profile.EstTimeAt(0.405));
        Assert.Equal(80.0, profile.EstLapTime);
    }
}
