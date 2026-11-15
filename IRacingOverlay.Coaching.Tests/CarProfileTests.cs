using IRacingOverlay.Coaching.Corners;

namespace IRacingOverlay.Coaching.Tests;

public sealed class CarProfileTests
{
    [Theory]
    [InlineData("Ferrari 296 GT3", CarClass.Gt)]
    [InlineData("Porsche 911 GT3 Cup (992)", CarClass.Gt)]
    [InlineData("BMW M4 GT4", CarClass.Gt)]
    [InlineData("Global Mazda MX-5 Cup", CarClass.LowPower)]
    [InlineData("Toyota GR86", CarClass.LowPower)]
    [InlineData("Ray FF1600", CarClass.LowPower)]
    [InlineData("FIA F4", CarClass.Formula)]
    [InlineData("Dallara F3", CarClass.Formula)]
    [InlineData("Super Formula SF23 - Toyota", CarClass.Formula)]
    [InlineData("Mercedes-AMG W13 E Performance", CarClass.Formula)]
    [InlineData(null, CarClass.Gt)]
    [InlineData("Some new car", CarClass.Gt)]
    public void TheCarPicksItsThresholds(string? car, CarClass expected) => Assert.Equal(expected, CarProfiles.For(car).Class);

    [Fact]
    public void ACornerIsSlowForTheCarItIsDrivenIn()
    {
        // A braked corner at 95 km/h: slow for a GT3, fast braking in an MX-5, slow for a formula car.
        var pass = CornerTypeTests.BrakingPass() with { MinSpeed = 95 / 3.6f };

        Assert.Equal(CornerType.Slow, CornerTypes.Classify(pass, CarProfiles.Gt));
        Assert.Equal(CornerType.BrakingFast, CornerTypes.Classify(pass, CarProfiles.LowPower));
        Assert.Equal(CornerType.Slow, CornerTypes.Classify(pass, CarProfiles.Formula));
        Assert.Equal(CornerType.BrakingFast, CornerTypes.Classify(pass with { MinSpeed = 140 / 3.6f }, CarProfiles.Formula));
    }

    [Fact]
    public void TheSessionUsesTheCarsProfile()
    {
        Assert.Equal(CarClass.LowPower, new CoachingSession(new CoachingOptions { Car = "Global Mazda MX-5 Cup" }).Profile.Class);
        Assert.Equal(CarClass.Gt, new CoachingSession().Profile.Class);
        Assert.Equal(CarClass.Formula, new CoachingSession(new CoachingOptions { Car = "MX-5", Profile = CarProfiles.Formula }).Profile.Class);
    }
}
