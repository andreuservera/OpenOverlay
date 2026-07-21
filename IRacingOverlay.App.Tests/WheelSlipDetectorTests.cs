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
    public void SuddenRpmSpike_FlagsSlipAfterDebounceWindow()
    {
        var detector = new WheelSlipDetector();

        for (var i = 0; i < 10; i++)
        {
            detector.Update(rpm: 3000, speedMps: 30, throttle: 0.8f, gear: 3);
        }

        // RPM jumps well above what 30 m/s in this gear implies — wheel spinning free of the road.
        // Requires two consecutive spiking ticks (debounce against single-sample noise).
        var firstSpikeTick = detector.Update(rpm: 4200, speedMps: 30, throttle: 0.8f, gear: 3);
        var secondSpikeTick = detector.Update(rpm: 4200, speedMps: 30, throttle: 0.8f, gear: 3);

        Assert.False(firstSpikeTick);
        Assert.True(secondSpikeTick);
    }

    [Fact]
    public void SingleTickNoiseSpike_DoesNotFlag()
    {
        var detector = new WheelSlipDetector();

        for (var i = 0; i < 10; i++)
        {
            detector.Update(rpm: 3000, speedMps: 30, throttle: 0.8f, gear: 3);
        }

        // One noisy tick above threshold, immediately followed by a return to normal.
        var noisy = detector.Update(rpm: 4200, speedMps: 30, throttle: 0.8f, gear: 3);
        var recovered = detector.Update(rpm: 3000, speedMps: 30, throttle: 0.8f, gear: 3);

        Assert.False(noisy);
        Assert.False(recovered);
    }

    [Fact]
    public void GearChange_DoesNotFalselyFlagSlip()
    {
        // Regression test for what live testing surfaced: corner exits bunch up gear shifts, and a
        // gear change alone shifts the RPM-to-speed ratio just as much as real wheelspin would. A
        // shared baseline across gears made every shift look like a slip event ("flashing too much on
        // corner exits"). Each gear now has its own learned baseline.
        var detector = new WheelSlipDetector();

        // Learn a stable baseline in 2nd gear.
        for (var i = 0; i < 10; i++)
        {
            detector.Update(rpm: 5000, speedMps: 20, throttle: 0.9f, gear: 2);
        }

        // Shift to 3rd: same speed, much lower RPM (as a normal upshift would produce) — the ratio
        // for 3rd hasn't been learned yet, so the first sample there must not be flagged as a spike.
        var afterUpshift = detector.Update(rpm: 3200, speedMps: 20, throttle: 0.9f, gear: 3);
        Assert.False(afterUpshift);

        // Continue steadily in 3rd — still shouldn't flag.
        var stillSteady = false;
        for (var i = 0; i < 10; i++)
        {
            stillSteady |= detector.Update(rpm: 3200 + i * 5, speedMps: 20 + i * 0.3f, throttle: 0.9f, gear: 3);
        }

        Assert.False(stillSteady);
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
