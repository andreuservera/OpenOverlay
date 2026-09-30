using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IRacingOverlay.App;

/// <summary>
/// One OpenOverlay per Windows session. A later launch asks the running copy to come forward and
/// exits; the running copy restores its own window, since only it can bring it back from the tray
/// without WPF losing track of the window's visibility.
/// </summary>
internal static class SingleInstance
{
    // Local\ is per logon session, so other signed-in users keep their own copy.
    private const string MutexName = @"Local\OpenOverlay.SingleInstance";
    private const string ActivateEventName = @"Local\OpenOverlay.Activate";

    // Held for the whole run: once its last handle closes the mutex is gone and a second copy could start.
    private static Mutex? _mutex;
    private static EventWaitHandle? _activate;

    /// <summary>True when this process is now the only instance. False when one is already running:
    /// it has been asked to come forward, and this process must exit.</summary>
    public static bool TryClaim()
    {
        try
        {
            // Opened before the mutex, so it exists whenever an owner does and no request is lost.
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            _mutex = new Mutex(false, MutexName);
        }
        catch (UnauthorizedAccessException)
        {
            // Created by a copy running elevated or as another user: it exists but can't be signalled.
            return false;
        }

        try
        {
            if (_mutex.WaitOne(TimeSpan.Zero))
            {
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died holding it; ownership has passed to this thread.
            return true;
        }

        RequestActivation();
        return false;
    }

    /// <summary>Runs <paramref name="onActivate"/> on a thread-pool thread each time a later launch
    /// asks this instance to come forward, including one made before this call.</summary>
    public static void ListenForActivation(Action onActivate) =>
        ThreadPool.RegisterWaitForSingleObject(_activate!, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);

    /// <summary>Must run on the thread that claimed the mutex.</summary>
    public static void Release() => _mutex?.ReleaseMutex();

    private static void RequestActivation()
    {
        // Windows only lets the app the user just launched take the foreground; hand that right to
        // the running copy, or its window would merely flash in the taskbar.
        using var current = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(current.ProcessName))
        {
            using (other)
            {
                if (other.Id != current.Id && other.SessionId == current.SessionId)
                {
                    AllowSetForegroundWindow(other.Id);
                }
            }
        }

        _activate!.Set();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
