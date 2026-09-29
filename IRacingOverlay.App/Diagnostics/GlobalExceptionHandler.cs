using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace IRacingOverlay.App.Diagnostics;

public enum StormAction
{
    None,

    /// <summary>Errors are arriving too fast to be one-offs: rebuild the overlay windows.</summary>
    Recover,

    /// <summary>Rebuilding didn't stop them: restart the process.</summary>
    Escalate,
}

/// <summary>Tells a burst of unhandled UI exceptions (a template or layout pass throwing every
/// frame) from an occasional one, and decides how hard to intervene.</summary>
public sealed class ExceptionStormDetector
{
    private readonly int _threshold;
    private readonly TimeSpan _window;
    private readonly TimeSpan _recoveryCooldown;
    private readonly int _maxRecoveries;
    private readonly TimeSpan _escalationWindow;
    private readonly Queue<DateTime> _recent = new();
    private readonly Queue<DateTime> _recoveries = new();
    private DateTime _lastActionUtc = DateTime.MinValue;

    public ExceptionStormDetector(
        int threshold = 20,
        TimeSpan? window = null,
        TimeSpan? recoveryCooldown = null,
        int maxRecoveries = 3,
        TimeSpan? escalationWindow = null)
    {
        _threshold = threshold;
        _window = window ?? TimeSpan.FromSeconds(10);
        _recoveryCooldown = recoveryCooldown ?? TimeSpan.FromSeconds(30);
        _maxRecoveries = maxRecoveries;
        _escalationWindow = escalationWindow ?? TimeSpan.FromMinutes(5);
    }

    public StormAction Record(DateTime nowUtc)
    {
        _recent.Enqueue(nowUtc);
        while (_recent.Count > 0 && nowUtc - _recent.Peek() > _window)
        {
            _recent.Dequeue();
        }

        if (_recent.Count < _threshold || nowUtc - _lastActionUtc < _recoveryCooldown)
        {
            return StormAction.None;
        }

        _recent.Clear();
        _lastActionUtc = nowUtc;
        while (_recoveries.Count > 0 && nowUtc - _recoveries.Peek() > _escalationWindow)
        {
            _recoveries.Dequeue();
        }

        if (_recoveries.Count >= _maxRecoveries)
        {
            _recoveries.Clear();
            return StormAction.Escalate;
        }

        _recoveries.Enqueue(nowUtc);
        return StormAction.Recover;
    }
}

/// <summary>
/// The last line of defence, installed before anything else runs. UI-thread exceptions (dispatcher
/// and WinForms tray callbacks) are logged and swallowed so one bad frame can't close the overlay
/// mid-race; a sustained burst triggers <see cref="StormRecovery"/>, then a process restart.
/// Exceptions that do end the process (background threads, fatal types) leave a crash report and
/// a replacement process behind.
/// </summary>
public static class GlobalExceptionHandler
{
    private static readonly object Gate = new();
    private static readonly ExceptionStormDetector Storm = new();
    private static long _handledCount;
    private static long _lastHandledTicks;
    private static int _terminating;
    private static Timer? _forcedExit;
    private static volatile string? _lastRef;

    /// <summary>Rebuilds the overlay windows. Set by the Control Panel once it exists.</summary>
    public static Action? StormRecovery { get; set; }

    public static long HandledCount => Interlocked.Read(ref _handledCount);

    /// <summary>Log reference of the latest contained UI exception.</summary>
    public static string? LastRef => _lastRef;

    public static DateTime? LastHandledUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastHandledTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>Process-wide handlers. Call first thing in Main, before any window or WinForms
    /// object exists (WinForms refuses to change its exception mode after that).</summary>
    public static void InstallProcessHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        try
        {
            // Without this, an exception in a tray-menu click shows WinForms' modal "Continue/Quit" dialog.
            Forms.Application.SetUnhandledExceptionMode(Forms.UnhandledExceptionMode.CatchException);
            Forms.Application.ThreadException += (_, e) => OnUiException("Tray", e.Exception);
        }
        catch (InvalidOperationException e)
        {
            AppLog.Warn("Crash handler", "Could not route WinForms exceptions", e);
        }
    }

    public static void InstallDispatcherHandler(Application app) =>
        app.DispatcherUnhandledException += OnDispatcherUnhandledException;

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (ExceptionPolicy.IsFatal(e.Exception))
        {
            // Left unhandled on purpose: the domain handler writes the crash report and restarts.
            AppLog.Critical("UI", "Fatal exception on the UI thread", e.Exception);
            return;
        }

        e.Handled = true;
        OnUiException("UI", e.Exception);
    }

    private static void OnUiException(string source, Exception exception)
    {
        Interlocked.Increment(ref _handledCount);
        Interlocked.Exchange(ref _lastHandledTicks, DateTime.UtcNow.Ticks);
        _lastRef = AppLog.Error(source, "Unhandled exception contained; the app keeps running", exception)?.Ref ?? _lastRef;

        StormAction action;
        lock (Gate)
        {
            action = Storm.Record(DateTime.UtcNow);
        }

        switch (action)
        {
            case StormAction.Recover:
                AppLog.Error(source, "Repeated unhandled exceptions; rebuilding the overlay windows");
                Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    try
                    {
                        StormRecovery?.Invoke();
                    }
                    catch (Exception recoveryFailure) when (!ExceptionPolicy.IsFatal(recoveryFailure))
                    {
                        AppLog.Error(source, "Rebuilding the overlay windows failed", recoveryFailure);
                    }
                });
                break;
            case StormAction.Escalate:
                RestartProcess("Unhandled exceptions kept recurring after the overlay windows were rebuilt", exception);
                break;
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        AppLog.Warn("Tasks", "Unobserved task exception", e.Exception);
    }

    /// <summary>Always terminal on .NET: all that is left is to leave evidence and a replacement.</summary>
    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (Interlocked.Exchange(ref _terminating, 1) == 1)
        {
            return;
        }

        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException(Convert.ToString(e.ExceptionObject, CultureInfo.InvariantCulture));
        AppLog.Critical("Crash", "Unhandled exception; the process is terminating", exception, new Dictionary<string, string>
        {
            ["thread"] = Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture),
        });
        var report = DiagnosticsReport.WriteCrashReport("Unhandled exception", exception);
        RunJournal.Current?.RecordCrash(report);
        AppRestarter.TryRelaunch(report is null ? "crash" : $"crash, report {System.IO.Path.GetFileName(report)}");
        AppLog.Flush(TimeSpan.FromSeconds(2));
    }

    /// <summary>Hands over to a fresh process when this one can't be brought back in place.</summary>
    public static void RestartProcess(string reason, Exception? exception)
    {
        if (Interlocked.Exchange(ref _terminating, 1) == 1)
        {
            return;
        }

        AppLog.Critical("Crash", reason, exception);
        var report = DiagnosticsReport.WriteCrashReport(reason, exception);
        if (!AppRestarter.TryRelaunch(reason))
        {
            // No replacement allowed: better a degraded overlay than none.
            Interlocked.Exchange(ref _terminating, 0);
            return;
        }

        RunJournal.Current?.RecordCrash(report);
        AppLog.Flush(TimeSpan.FromSeconds(2));
        // A normal shutdown saves widget positions; the timer covers a UI thread too broken to get there.
        _forcedExit = new Timer(_ => Process.GetCurrentProcess().Kill(), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        Application.Current?.Dispatcher.BeginInvoke(() => Application.Current?.Shutdown());
    }
}
