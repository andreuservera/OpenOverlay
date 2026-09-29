using System.Globalization;
using System.Windows;
using IRacingOverlay.App.Diagnostics;
using Velopack;

namespace IRacingOverlay.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Public GitHub repo used both as the update source (below) and to build release download
    /// links in the README — kept in one place so a repo rename/fork only needs updating here.
    /// </summary>
    public const string GitHubRepoUrl = "https://github.com/andreuservera/OpenOverlay";

    /// <summary>
    /// WPF normally auto-generates Main() from App.xaml's StartupUri. Velopack needs a real, explicit
    /// entry point instead: VelopackApp.Build().Run() must execute before any other app code, since
    /// on first run after an install/update it briefly runs special hooks (e.g. creating shortcuts)
    /// and then exits immediately rather than continuing into the app proper.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        // Before anything else can throw: every later failure must land in the log.
        GlobalExceptionHandler.InstallProcessHandlers();
        AppRestarter.WaitForPreviousInstance(args);

        try
        {
            VelopackApp.Build().Run();
        }
        catch (Exception e)
        {
            // Install hooks and pending updates are a convenience; the overlay has to start regardless.
            AppLog.Error("Startup", "Velopack startup failed; continuing without update support", e);
        }

        AppInfo.IsRecoveredLaunch = AppRestarter.IsRecoveredLaunch(args);
        AppInfo.PreviousRunId = AppRestarter.PreviousRunId(args);
        AppInfo.Initialize();
        AppLog.Initialize();
        AppLog.Info("Startup", "OpenOverlay starting", AppInfo.Describe());
        RecordEarlierRuns();

        var app = new App();
        GlobalExceptionHandler.InstallDispatcherHandler(app);
        app.InitializeComponent();
        app.Run();
    }

    /// <summary>Puts runs that never shut down into this run's log, so a log that stops abruptly
    /// is explained by the next one rather than left as a mystery.</summary>
    private static void RecordEarlierRuns()
    {
        RunJournal.Current = new RunJournal(
            System.IO.Path.Combine(AppLog.LogDirectory, "runs"),
            new RunRecord(AppInfo.RunId, AppInfo.ProcessId, AppInfo.StartedUtc, AppInfo.Version));
        var (unclean, stillRunning) = RunJournal.Current.Begin();
        foreach (var run in unclean)
        {
            AppLog.Warn(
                "Startup",
                run.CrashReport is { } report
                    ? $"Run {run.RunId} crashed; see {report}"
                    : $"Run {run.RunId} ended without shutting down (killed, a fault no handler can catch, or power loss)",
                data: new Dictionary<string, string>
                {
                    ["run"] = run.RunId,
                    ["version"] = run.Version,
                    ["started"] = run.StartedUtc.ToString("O", CultureInfo.InvariantCulture),
                });
        }

        foreach (var run in stillRunning)
        {
            AppLog.Warn("Startup", $"Another OpenOverlay instance is running (run {run.RunId}, pid {run.ProcessId})");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var problems = AppLog.Problems();
        AppLog.Info("Shutdown", "OpenOverlay exiting", new Dictionary<string, string>
        {
            ["exitCode"] = e.ApplicationExitCode.ToString(CultureInfo.InvariantCulture),
            ["uptime"] = AppInfo.Uptime.ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture),
            ["errors"] = problems.Where(p => p.Level >= LogLevel.Error).Sum(p => p.Count).ToString(CultureInfo.InvariantCulture),
            ["warnings"] = problems.Where(p => p.Level == LogLevel.Warning).Sum(p => p.Count).ToString(CultureInfo.InvariantCulture),
            ["topProblem"] = problems.FirstOrDefault() is { } top ? $"{top.Source}: {top.Message} x{top.Count}" : "none",
        });
        RunJournal.Current?.End();
        AppLog.Shutdown(TimeSpan.FromSeconds(2));
        base.OnExit(e);
    }
}
