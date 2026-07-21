using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class WheelSlipDetectorTests
{
    [Fact]
    public void StableRatio_NeverFlagsSlip()
    {
        var detector = new WheelSlipDetector();

        // Steady 3000 RPM at 30 m/s in 3rd gear under power, repeated — a stable ratio should never trip.
        for (var i = 0; i < 20; i++)
        {
            Assert.False(detector.Update(rpm: 3000, speedMps: 30, throttle: 0.8f, gear: 3));
        }
    }

    [Fact]
    public void SuddenRpmSpike_FlagsSlip()
    {
        var detector = new WheelSlipDetector();

        for (var i = 0; i < 10; i++)
        {
            detector.Update(rpm: 3000, speedMps: 30, throttle: 0.8f, gear: 3);
        }

        // RPM jumps well above what 30 m/s in this gear implies — wheel spinning free of the road.
        var slipping = detector.Update(rpm: 4200, speedMps: 30, throttle: 0.8f, gear: 3);

        Assert.True(slipping);
    }

    [Fact]
    public void GradualAcceleration_DoesNotFalselyFlag()
    {
        var detector = new WheelSlipDetector();
        var anyFlagged = false;

        // RPM and speed rising together in proportion (a normal gear pull) shouldn't trip the detector.
        for (var i = 0; i < 30; i++)
        {
            var speed = 10 + i * 0.8f;
            var rpm = speed * 100f; // constant ratio throughout
            anyFlagged |= detector.Update(rpm, speed, throttle: 0.9f, gear: 3);
        }

        Assert.False(anyFlagged);
    }

    [Theory]
    [InlineData(0)]   // neutral
    [InlineData(-1)]  // reverse
    public void NonForwardGear_NeverFlagsSlip(int gear)
    {
        var detector = new WheelSlipDetector();

        var slipping = detector.Update(rpm: 6000, speedMps: 5, throttle: 0.9f, gear: gear);

        Assert.False(slipping);
    }

    [Fact]
    public void OffThrottle_NeverFlagsSlip()
    {
        var detector = new WheelSlipDetector();
        for (var i = 0; i < 10; i++)
        {
            detector.Update(rpm: 3000, speedMps: 30, throttle: 0.8f, gear: 3);
        }

        // Same RPM spike as the slip test, but off-throttle (e.g. engine braking) shouldn't count.
        var slipping = detector.Update(rpm: 4200, speedMps: 30, throttle: 0.05f, gear: 3);

        Assert.False(slipping);
    }
}
