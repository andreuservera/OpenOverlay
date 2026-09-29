using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.Tests;

public class ComponentGuardTests
{
    private sealed class Clock
    {
        public DateTime Now { get; set; } = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    }

    private static void Fail() => throw new InvalidOperationException("widget bug");

    [Fact]
    public void Run_Success_IsHealthy()
    {
        var guard = new ComponentGuard("Widget: Fuel");

        Assert.True(guard.Run(() => { }));
        Assert.Equal(HealthStatus.Healthy, guard.Health.Status);
    }

    [Fact]
    public void Run_Failure_IsContainedAndDegrades()
    {
        var guard = new ComponentGuard("Widget: Fuel");

        Assert.False(guard.Run(Fail));

        var health = guard.Health;
        Assert.Equal(HealthStatus.Degraded, health.Status);
        Assert.Equal(1, health.Failures);
        Assert.Contains("widget bug", health.LastError);
    }

    [Fact]
    public void Run_RepeatedFailures_TripRecoveryAndPauseForTheCooldown()
    {
        var clock = new Clock();
        var recovered = new List<GuardStage>();
        var guard = new ComponentGuard("Widget: Fuel", recovered.Add, () => clock.Now, failureThreshold: 3);

        guard.Run(Fail, GuardStage.Render);
        guard.Run(Fail, GuardStage.Render);
        Assert.Empty(recovered);
        guard.Run(Fail, GuardStage.Render);

        Assert.Equal([GuardStage.Render], recovered);
        Assert.Equal(HealthStatus.Failed, guard.Health.Status);
        var calls = 0;
        Assert.False(guard.Run(() => calls++));
        Assert.Equal(0, calls);

        clock.Now += TimeSpan.FromSeconds(1.1);
        Assert.True(guard.Run(() => calls++));
        Assert.Equal(1, calls);
        Assert.Equal(HealthStatus.Degraded, guard.Health.Status);

        clock.Now += TimeSpan.FromMinutes(2);
        Assert.Equal(HealthStatus.Healthy, guard.Health.Status);
    }

    [Fact]
    public void Cooldown_DoublesOnEveryTripUpToTheMaximum()
    {
        var clock = new Clock();
        var guard = new ComponentGuard("Widget: Fuel", null, () => clock.Now, failureThreshold: 1,
            baseCooldown: TimeSpan.FromSeconds(1), maxCooldown: TimeSpan.FromSeconds(4));

        var cooldowns = new List<double>();
        for (var trip = 0; trip < 5; trip++)
        {
            guard.Run(Fail);
            var paused = TimeSpan.Zero;
            while (guard.IsCoolingDown)
            {
                clock.Now += TimeSpan.FromMilliseconds(250);
                paused += TimeSpan.FromMilliseconds(250);
            }

            cooldowns.Add(paused.TotalSeconds);
        }

        Assert.Equal([1, 2, 4, 4, 4], cooldowns);
    }

    [Fact]
    public void Stages_CountSeparately_SoAWorkingBuildCannotMaskAFailingRender()
    {
        var recovered = new List<GuardStage>();
        var guard = new ComponentGuard("Widget: Fuel", recovered.Add, failureThreshold: 3);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(guard.TryRun(() => 42, out _, GuardStage.Build));
            guard.Run(Fail, GuardStage.Render);
        }

        Assert.Equal([GuardStage.Render], recovered);
    }

    [Fact]
    public void TryRun_ReturnsTheValueOnSuccess()
    {
        var guard = new ComponentGuard("Standings model");

        Assert.True(guard.TryRun(() => "rows", out var value));
        Assert.Equal("rows", value);
        Assert.False(guard.TryRun<string>(() => throw new IndexOutOfRangeException(), out _));
    }

    [Fact]
    public void FailingRecovery_IsContained()
    {
        var guard = new ComponentGuard("Dashboard", _ => throw new InvalidOperationException("recovery bug"), failureThreshold: 1);

        guard.Run(Fail);

        Assert.Equal(HealthStatus.Failed, guard.Health.Status);
    }

    [Fact]
    public void FatalExceptions_AreNotSwallowed()
    {
        var guard = new ComponentGuard("Widget: Fuel");

        Assert.Throws<OutOfMemoryException>(() => guard.Run(() => throw new OutOfMemoryException()));
    }
}

[Collection("Diagnostics statics")]
public class HealthMonitorTests
{
    [Fact]
    public void Snapshot_OverallIsTheWorstComponent()
    {
        var monitor = new HealthMonitor();
        var guard = monitor.CreateGuard("Widget: Fuel");
        monitor.Report("Telemetry", HealthStatus.Healthy, "connected");
        monitor.Report("Updates", HealthStatus.Degraded, "offline");

        Assert.Equal(HealthStatus.Degraded, monitor.Snapshot().Overall);

        guard.Run(() => throw new InvalidOperationException());
        guard.Run(() => throw new InvalidOperationException());
        guard.Run(() => throw new InvalidOperationException());
        var report = monitor.Snapshot();

        Assert.Equal(HealthStatus.Failed, report.Overall);
        Assert.StartsWith("Widget: Fuel:", report.Summary);
        Assert.Same(report, HealthMonitor.Latest);
    }

    [Fact]
    public void Report_KeepsTheSinceTimeWhileTheStatusHolds()
    {
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new HealthMonitor(() => now);

        monitor.Report("Telemetry", HealthStatus.Degraded, "stalled");
        now += TimeSpan.FromSeconds(30);
        monitor.Report("Telemetry", HealthStatus.Degraded, "stalled");
        var held = monitor.Snapshot().Components.Single();
        now += TimeSpan.FromSeconds(30);
        monitor.Report("Telemetry", HealthStatus.Healthy, "connected");
        var changed = monitor.Snapshot().Components.Single();

        Assert.Equal(now - TimeSpan.FromSeconds(60), held.SinceUtc);
        Assert.Equal(now, changed.SinceUtc);
    }

    [Fact]
    public void Summary_AllHealthy()
    {
        var monitor = new HealthMonitor();
        monitor.Report("Telemetry", HealthStatus.Healthy, "connected");

        Assert.Equal("All systems healthy", monitor.Snapshot().Summary);
    }
}
