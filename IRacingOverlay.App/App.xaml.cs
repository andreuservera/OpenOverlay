using System.Windows;
using Velopack;
using Velopack.Sources;

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
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = CheckForUpdatesAsync();
    }

    /// <summary>
    /// Silent, best-effort background check — runs once per launch, never blocks or interrupts the
    /// user. If a newer release is published on GitHub, it's downloaded in the background and
    /// applied automatically the *next* time the app restarts (not the current session), so it never
    /// yanks widgets away mid-race. Any failure (offline, GitHub unreachable, running a dev build that
    /// wasn't installed via the Velopack installer) is swallowed — update-checking is a convenience,
    /// never something that should be able to break a normal launch.
    /// </summary>
    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false));
            if (!manager.IsInstalled)
            {
                return; // running from a loose dev build, not a Velopack install — nothing to update
            }

            var newVersion = await manager.CheckForUpdatesAsync();
            if (newVersion is null)
            {
                return;
            }

            await manager.DownloadUpdatesAsync(newVersion);
            manager.WaitExitThenApplyUpdates(newVersion, restart: false);
        }
        catch
        {
            // Offline, GitHub unreachable, etc. — silently skip; the app works fine either way.
        }
    }
}
