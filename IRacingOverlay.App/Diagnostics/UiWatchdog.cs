using System.Diagnostics;
using System.Globalization;

namespace IRacingOverlay.App.Diagnostics;

/// <summary>
/// Watches the UI thread from a thread of its own. Every widget is drawn on the UI thread, so a
/// blocked UI thread means every overlay is frozen on screen — invisible to anything running on
/// that thread. The update loop calls <see cref="Beat"/> each tick; a stall is logged, reported as
/// Failed, and past <c>restartAfter</c> the process is replaced (the overlay is useless by then).
/// </summary>
public sealed class UiWatchdog : IDisposable
{
    private const int PollMs = 1000;
    private const long SuspendGapMs = 5000;

    private readonly long _degradedAfterMs;
    private readonly long _failedAfterMs;
    private readonly long _restartAfterMs;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;
    private long _lastBeatMs = Environment.TickCount64;
    private long _worstStallMs;
    private volatile bool _armed;
    private bool _hangReported;
    private bool _restartAttempted;

    public UiWatchdog(TimeSpan? degradedAfter = null, TimeSpan? failedAfter = null, TimeSpan? restartAfter = null)
    {
        _degradedAfterMs = (long)(degradedAfter ?? TimeSpan.FromSeconds(3)).TotalMilliseconds;
        _failedAfterMs = (long)(failedAfter ?? TimeSpan.FromSeconds(10)).TotalMilliseconds;
        _restartAfterMs = (long)(restartAfter ?? TimeSpan.FromSeconds(60)).TotalMilliseconds;
        _thread = new Thread(Run) { IsBackground = true, Name = "OpenOverlay UI watchdog" };
    }

    public TimeSpan Stall => TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - Volatile.Read(ref _lastBeatMs)));

    public HealthStatus Status
    {
        get
        {
            if (!_armed)
            {
                return HealthStatus.Healthy;
            }

            var stall = Stall.TotalMilliseconds;
            return stall >= _failedAfterMs ? HealthStatus.Failed
                : stall >= _degradedAfterMs ? HealthStatus.Degraded
                : HealthStatus.Healthy;
        }
    }

    public void Start() => _thread.Start();

    /// <summary>Called by the update loop. The first beat arms the watchdog, so however long startup
    /// takes is never mistaken for a hang.</summary>
    public void Beat()
    {
        Volatile.Write(ref _lastBeatMs, Environment.TickCount64);
        _armed = true;
    }

    public void Dispose()
    {
        _stop.Set();
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void Run()
    {
        var lastPollMs = Environment.TickCount64;
        while (!_stop.Wait(PollMs))
        {
            var now = Environment.TickCount64;
            if (now - lastPollMs > PollMs + SuspendGapMs)
            {
                // This thread wasn't scheduled either — sleep, hibernate or a debugger break. Not a hang.
                Volatile.Write(ref _lastBeatMs, now);
            }

            lastPollMs = now;
            if (!_armed)
            {
                continue;
            }

            try
            {
                Check(now - Volatile.Read(ref _lastBeatMs));
            }
            catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
            {
                AppLog.Error("UI watchdog", "Watchdog check failed", e);
            }
        }
    }

    private void Check(long stallMs)
    {
        if (stallMs >= _failedAfterMs)
        {
            _worstStallMs = Math.Max(_worstStallMs, stallMs);
            if (!_hangReported)
            {
                _hangReported = true;
                AppLog.Error("UI watchdog", "UI thread unresponsive; overlays are frozen", data: Seconds("stalledSeconds", stallMs));
            }

            if (stallMs >= _restartAfterMs && !_restartAttempted && !Debugger.IsAttached)
            {
                _restartAttempted = true;
                GlobalExceptionHandler.RestartProcess(
                    $"UI thread unresponsive for {stallMs / 1000} s",
                    new TimeoutException("The UI thread stopped processing its update loop."));
            }
        }
        else if (_hangReported && stallMs < _degradedAfterMs)
        {
            _hangReported = false;
            _restartAttempted = false;
            AppLog.Warn("UI watchdog", "UI thread responsive again", data: Seconds("stalledSeconds", _worstStallMs));
            _worstStallMs = 0;
        }
    }

    private static Dictionary<string, string> Seconds(string key, long milliseconds) =>
        new() { [key] = (milliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) };
}
