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
        AppInfo.Initialize();
        AppLog.Initialize();
        AppLog.Info("Startup", "OpenOverlay starting", AppInfo.Describe());

        var app = new App();
        GlobalExceptionHandler.InstallDispatcherHandler(app);
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("Shutdown", "OpenOverlay exiting", new Dictionary<string, string> { ["exitCode"] = e.ApplicationExitCode.ToString() });
        AppLog.Shutdown(TimeSpan.FromSeconds(2));
        base.OnExit(e);
    }
}
