namespace IRacingOverlay.App.Diagnostics;

/// <summary>
/// Makes sure quitting quits. Once the control panel has closed to exit, the process has nothing
/// left to show; if it is still running a few seconds later, something on the way out is stuck
/// (seen in the field: the window gone, no tray icon, the process idle for over a minute). The
/// guard logs the last shutdown step reached, so the cause can be found, and ends the process.
/// </summary>
internal static class ShutdownGuard
{
    private static volatile string _step = "not started";
    private static int _armed;

    /// <summary>The shutdown step now running, named in the log if the exit gets stuck.</summary>
    public static void Step(string step) => _step = step;

    /// <summary>Starts the countdown; later calls do nothing.</summary>
    public static void Arm(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _armed, 1) == 1)
        {
            return;
        }

        new Thread(() =>
        {
            Thread.Sleep(timeout);
            AppLog.Error("Shutdown", $"Still running {timeout.TotalSeconds:0} s after the control panel closed; forcing exit",
                data: new Dictionary<string, string> { ["lastStep"] = _step });
            RunJournal.Current?.End();
            AppLog.Shutdown(TimeSpan.FromSeconds(2));
            Environment.Exit(0);
        })
        {
            IsBackground = true,
            Name = "OpenOverlay shutdown guard",
        }.Start();
    }
}
