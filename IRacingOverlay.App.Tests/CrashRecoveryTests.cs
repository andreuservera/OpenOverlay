using System.IO;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Tests;

public class CrashRecoveryTests
{
    private static readonly DateTime Start = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StormDetector_OccasionalExceptions_AreLeftAlone()
    {
        var detector = new ExceptionStormDetector(threshold: 20, window: TimeSpan.FromSeconds(10));

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(StormAction.None, detector.Record(Start + TimeSpan.FromSeconds(i)));
        }
    }

    [Fact]
    public void StormDetector_Burst_RecoversThenEscalatesWhenItKeepsComingBack()
    {
        var detector = new ExceptionStormDetector(
            threshold: 5, window: TimeSpan.FromSeconds(10), recoveryCooldown: TimeSpan.FromSeconds(30),
            maxRecoveries: 2, escalationWindow: TimeSpan.FromMinutes(5));
        var now = Start;

        StormAction Burst()
        {
            var action = StormAction.None;
            for (var i = 0; i < 5; i++)
            {
                now += TimeSpan.FromMilliseconds(100);
                var result = detector.Record(now);
                if (result != StormAction.None)
                {
                    action = result;
                }
            }

            return action;
        }

        Assert.Equal(StormAction.Recover, Burst());
        // Inside the cooldown: the rebuild gets a chance to work before anything else happens.
        Assert.Equal(StormAction.None, Burst());
        now += TimeSpan.FromSeconds(31);
        Assert.Equal(StormAction.Recover, Burst());
        now += TimeSpan.FromSeconds(31);
        Assert.Equal(StormAction.Escalate, Burst());
    }

    [Fact]
    public void StormDetector_RecoveriesFarApart_NeverEscalate()
    {
        var detector = new ExceptionStormDetector(threshold: 2, maxRecoveries: 1, escalationWindow: TimeSpan.FromMinutes(5));

        for (var storm = 0; storm < 5; storm++)
        {
            var at = Start + TimeSpan.FromMinutes(10 * storm);
            detector.Record(at);
            Assert.Equal(StormAction.Recover, detector.Record(at + TimeSpan.FromMilliseconds(10)));
        }
    }

    [Fact]
    public void RestartHistory_AllowsThreeRestartsPerTenMinutes()
    {
        var history = Path.Combine(Path.GetTempPath(), $"oo-restarts-{Guid.NewGuid():N}.txt");
        try
        {
            Assert.True(AppRestarter.RecordAttempt(Start, history));
            Assert.True(AppRestarter.RecordAttempt(Start.AddMinutes(1), history));
            Assert.True(AppRestarter.RecordAttempt(Start.AddMinutes(2), history));
            Assert.False(AppRestarter.RecordAttempt(Start.AddMinutes(3), history));

            // The oldest attempt ages out of the window.
            Assert.True(AppRestarter.RecordAttempt(Start.AddMinutes(10.5), history));
        }
        finally
        {
            File.Delete(history);
        }
    }

    [Fact]
    public void RestartHistory_IgnoresGarbage()
    {
        var history = Path.Combine(Path.GetTempPath(), $"oo-restarts-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(history, ["not a number", "-5", "99999999999999999999999"]);
        try
        {
            Assert.True(AppRestarter.RecordAttempt(Start, history));
        }
        finally
        {
            File.Delete(history);
        }
    }

    [Fact]
    public void RecoveredLaunch_IsRecognisedFromTheArguments()
    {
        Assert.True(AppRestarter.IsRecoveredLaunch(["--recovered", "--wait-for-pid", "123"]));
        Assert.False(AppRestarter.IsRecoveredLaunch([]));
    }

    [Fact]
    public void WaitForPreviousInstance_ReturnsForMissingOrInvalidPids()
    {
        AppRestarter.WaitForPreviousInstance(["--wait-for-pid", "not-a-pid"]);
        AppRestarter.WaitForPreviousInstance(["--wait-for-pid", int.MaxValue.ToString()]);
        AppRestarter.WaitForPreviousInstance(["--wait-for-pid"]);
    }

    [Fact]
    public void UiWatchdog_StatusFollowsTheTimeSinceTheLastBeat()
    {
        using var watchdog = new UiWatchdog(
            degradedAfter: TimeSpan.FromMilliseconds(150),
            failedAfter: TimeSpan.FromMilliseconds(600),
            restartAfter: TimeSpan.FromHours(1));

        watchdog.Beat();
        Assert.Equal(HealthStatus.Healthy, watchdog.Status);

        Thread.Sleep(300);
        Assert.Equal(HealthStatus.Degraded, watchdog.Status);

        Thread.Sleep(500);
        Assert.Equal(HealthStatus.Failed, watchdog.Status);

        watchdog.Beat();
        Assert.Equal(HealthStatus.Healthy, watchdog.Status);
    }
}
