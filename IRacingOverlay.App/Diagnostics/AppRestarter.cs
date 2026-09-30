using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace IRacingOverlay.App.Diagnostics;

/// <summary>
/// Starts a fresh copy of OpenOverlay when this one cannot continue (a fatal crash or a hung UI
/// thread), so the overlays come back mid-race without the driver doing anything. The new process
/// waits for this one to exit before it starts, and a restart history stops a crash loop: after
/// three restarts in ten minutes the app stays down and the crash report is left for the user.
/// </summary>
public static class AppRestarter
{
    public const string RecoveredArgument = "--recovered";
    private const string WaitForPidArgument = "--wait-for-pid";
    private const string PreviousRunArgument = "--previous-run";
    private const int MaxRestarts = 3;
    private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WaitForPrevious = TimeSpan.FromSeconds(15);
    private static readonly string HistoryPath = Path.Combine(AppLog.LogDirectory, "restarts.txt");
    private static int _relaunched;

    public static bool IsRecoveredLaunch(string[] args) => args.Contains(RecoveredArgument, StringComparer.OrdinalIgnoreCase);

    /// <summary>The run ID of the process that relaunched this one, linking the two in the logs.</summary>
    public static string? PreviousRunId(string[] args) => ValueAfter(args, PreviousRunArgument);

    /// <summary>Blocks until the process that relaunched us has exited, so the two never hold the
    /// tray icon, the hotkeys or the settings files at the same time.</summary>
    public static void WaitForPreviousInstance(string[] args)
    {
        if (!int.TryParse(ValueAfter(args, WaitForPidArgument), CultureInfo.InvariantCulture, out var pid))
        {
            return;
        }

        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(WaitForPrevious);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    /// <summary>Starts the replacement process. False when not allowed (debugger attached, dev host,
    /// restart limit reached, already relaunched) or when starting it failed.</summary>
    public static bool TryRelaunch(string reason)
    {
        if (Interlocked.Exchange(ref _relaunched, 1) == 1)
        {
            return false;
        }

        try
        {
            var path = Environment.ProcessPath;
            if (Debugger.IsAttached || path is null
                || string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Warn("Restart", "Automatic restart skipped (debugger or development host)", data: Reason(reason));
                return false;
            }

            if (!RecordAttempt(DateTime.UtcNow))
            {
                AppLog.Critical("Restart", $"Automatic restart skipped: {MaxRestarts} restarts within {RestartWindow.TotalMinutes:0} minutes", data: Reason(reason));
                return false;
            }

            var start = new ProcessStartInfo(path) { UseShellExecute = false };
            start.ArgumentList.Add(RecoveredArgument);
            start.ArgumentList.Add(WaitForPidArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(PreviousRunArgument);
            start.ArgumentList.Add(AppInfo.RunId);
            using var _ = Process.Start(start);
            AppLog.Critical("Restart", "Started a replacement OpenOverlay process", data: Reason(reason));
            return true;
        }
        catch (Exception e)
        {
            AppLog.Critical("Restart", "Automatic restart failed", e, Reason(reason));
            return false;
        }
    }

    /// <summary>Appends this attempt to the history unless the window already holds the maximum.</summary>
    internal static bool RecordAttempt(DateTime nowUtc, string? historyPath = null)
    {
        historyPath ??= HistoryPath;
        var recent = new List<DateTime>();
        try
        {
            if (File.Exists(historyPath))
            {
                foreach (var line in File.ReadAllLines(historyPath))
                {
                    if (long.TryParse(line, CultureInfo.InvariantCulture, out var ticks)
                        && ticks > 0 && ticks <= DateTime.MaxValue.Ticks
                        && nowUtc - new DateTime(ticks, DateTimeKind.Utc) < RestartWindow)
                    {
                        recent.Add(new DateTime(ticks, DateTimeKind.Utc));
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable history must not block recovery; the attempt below still counts.
        }

        if (recent.Count >= MaxRestarts)
        {
            return false;
        }

        recent.Add(nowUtc);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            File.WriteAllLines(historyPath, recent.Select(t => t.Ticks.ToString(CultureInfo.InvariantCulture)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Worst case the loop guard forgets this attempt.
        }

        return true;
    }

    private static Dictionary<string, string> Reason(string reason) => new() { ["reason"] = reason };

    private static string? ValueAfter(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
