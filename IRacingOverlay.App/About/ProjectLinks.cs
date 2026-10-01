using System.Diagnostics;

namespace IRacingOverlay.App.About;

/// <summary>Where OpenOverlay lives online. Everything derives from <see cref="App.GitHubRepoUrl"/>,
/// so a fork or a rename changes one constant.</summary>
public static class ProjectLinks
{
    public static string Repository => App.GitHubRepoUrl;

    public static string Documentation => $"{Repository}#readme";

    public static string NewIssue => $"{Repository}/issues/new";

    public static string Releases => $"{Repository}/releases";

    public static string Changelog => $"{Repository}/blob/main/CHANGELOG.md";

    public static string License => $"{Repository}/blob/main/LICENSE";

    /// <summary>No community server exists yet: the About page lists it as coming soon until this is set.</summary>
    public static string? Discord => null;

    public static string Release(ReleaseVersion version) => $"{Releases}/tag/v{version}";

    /// <summary>Opens a link in the default browser.</summary>
    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
