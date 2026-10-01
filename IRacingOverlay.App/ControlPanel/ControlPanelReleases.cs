using System.Globalization;
using System.Windows.Input;
using IRacingOverlay.App.About;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// The ABOUT section of the rail — What's New, the Changelog and About OpenOverlay — and the
/// version in the status bar. What they show comes from three places: the changelog this build
/// embeds (<see cref="ReleaseCatalog"/>), the build's own stamp (<see cref="BuildInfo"/>) and this
/// PC's install history (<see cref="InstallHistory"/>).
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private const string AboutGroup = "ABOUT";
    private const string WhatsNewPageKey = "app.whatsnew";
    private const string ChangelogPageKey = "app.changelog";
    private const string AboutPageKey = "app.about";

    private string _updateStatus = "Not checked yet";
    private InfoSetting? _updateStatusRow;
    private RelayCommand? _showAbout;

    /// <summary>The status bar's version: always on screen, one click from the About page.</summary>
    public string VersionLabel => $"v{BuildInfo.Version}";

    public string VersionToolTip => $"{BuildInfo.Summary}\nClick for version details, what's new and the changelog.";

    /// <summary>Under the logo in the side panel of the ABOUT pages.</summary>
    public string VersionHeadline => $"Version {BuildInfo.Version} · {BuildInfo.Channel}";

    public ICommand ShowAboutCommand => _showAbout ??= new RelayCommand(() =>
    {
        AppLog.Activity("Control Panel", "Version in the status bar clicked");
        Navigate(AboutPageKey);
    });

    /// <summary>An ABOUT page: nothing to preview, so the side panel shows the product instead.</summary>
    public bool IsInfoPage => _selected?.Group == AboutGroup;

    public bool ShowsPreviewPlaceholder => !HasPreview && !IsInfoPage;

    /// <summary>The update check's latest state, pushed by MainWindow once a second.</summary>
    public string UpdateStatus
    {
        get => _updateStatus;
        set
        {
            if (_updateStatus == value)
            {
                return;
            }

            _updateStatus = value;
            if (_updateStatusRow is not null)
            {
                _updateStatusRow.Value = value;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Opens What's New: the update notice's "View details".</summary>
    public void ShowWhatsNew() => Navigate(WhatsNewPageKey);

    private void AddAboutPages()
    {
        NavItems.Add(NavItem.ForPage(
            WhatsNewPageKey, "What's New", "The changes in the version you're running.",
            "M12,3 L13.8,10.2 L21,12 L13.8,13.8 L12,21 L10.2,13.8 L3,12 L10.2,10.2 Z",
            AboutGroup));

        NavItems.Add(NavItem.ForPage(
            ChangelogPageKey, "Changelog", "Every release so far, newest first.",
            "M3,12 A9,9 0 1 0 12,3 A9.75,9.75 0 0 0 5.26,5.74 L3,8 M3,3 V8 H8 M12,7 V12 L16,14",
            AboutGroup));

        NavItems.Add(NavItem.ForPage(
            AboutPageKey, "About OpenOverlay", "Version, build, update channel, links and support.",
            "M12,3 A9,9 0 1 0 12.01,3 Z M12,11 V16.5 M12,7.5 V7.51",
            AboutGroup));
    }

    private void Navigate(string key)
    {
        // A search that hides the page would leave the rail without a selection.
        SearchText = "";
        Selected = NavItems.First(item => item.Key == key);
    }

    // ===== What's New =====

    private IEnumerable<SettingsGroup> WhatsNewPage()
    {
        var entry = ReleaseCatalog.Current;
        if (entry is null)
        {
            return
            [
                new SettingsGroup(
                    $"VERSION {BuildInfo.Version}",
                    "This build carries no release notes. Every release is described on GitHub."),
                ReleaseLinks(null),
            ];
        }

        var groups = new List<SettingsGroup> { new($"WHAT'S NEW IN {entry.Version}", WhatsNewSubtitle(entry)) };
        foreach (var section in entry.Sections)
        {
            var group = new SettingsGroup(SectionTitle(section)) { IsWarning = section.IsWarning };
            foreach (var item in section.Items)
            {
                group.Items.Add(new BulletSetting(item));
            }

            groups.Add(group);
        }

        groups.Add(ReleaseLinks(entry));
        return groups;
    }

    private static string WhatsNewSubtitle(ChangelogEntry entry)
    {
        var parts = new List<string>();
        if (entry.Date is { } released)
        {
            parts.Add($"Released {FormatDate(released)}.");
        }

        if (InstallHistory.InstalledUtc is { } installed)
        {
            parts.Add($"On this PC since {FormatDate(installed)}.");
        }

        if (entry.Summary is { } summary)
        {
            parts.Add(summary);
        }

        return string.Join(' ', parts);
    }

    private static string SectionTitle(ChangelogSection section) =>
        section.Title.Length > 0 ? section.Title.ToUpperInvariant() : "CHANGES";

    private SettingsGroup ReleaseLinks(ChangelogEntry? entry) => new SettingsGroup("MORE")
        .With(
            new ActionSetting("Full changelog", "Every release so far, newest first.", "Open", () => Navigate(ChangelogPageKey)),
            entry is not null
                ? new ActionSetting("Release page", $"{entry.Version} on GitHub, with its downloads.", "Open", () => ProjectLinks.Open(ProjectLinks.Release(entry.Version)))
                : new ActionSetting("Releases", "Every release on GitHub, with its downloads.", "Open", () => ProjectLinks.Open(ProjectLinks.Releases)));

    // ===== Changelog =====

    private IEnumerable<SettingsGroup> ChangelogPage()
    {
        var entries = ReleaseCatalog.Changelog.Entries;
        var releases = new SettingsGroup(
            "RELEASES",
            entries.Count == 0
                ? "This build carries no release notes. Every release is described on GitHub."
                : "Newest first. Select a version to show or hide its changes.");

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            releases.Items.Add(new ReleaseSetting(
                $"v{entry.Version}",
                entry.Date is { } date ? FormatDate(date) : null,
                i == 0 ? "YOUR VERSION" : null,
                entry.Summary,
                entry.Sections.Select(section => new NoteSection(SectionTitle(section), section.Items, section.IsWarning)).ToList(),
                isExpanded: i == 0));
        }

        return
        [
            releases,
            new SettingsGroup("MORE").With(
                new ActionSetting("Changelog on GitHub", "The same history, as the project keeps it.", "Open", () => ProjectLinks.Open(ProjectLinks.Changelog)),
                new ActionSetting("Releases", "Every release on GitHub, with its downloads.", "Open", () => ProjectLinks.Open(ProjectLinks.Releases))),
        ];
    }

    // ===== About =====

    private IEnumerable<SettingsGroup> AboutPage()
    {
        var updateStatusRow = new InfoSetting("Updates", "The last automatic check for a new version.", _updateStatus);
        if (!_buildingIndex)
        {
            _updateStatusRow = updateStatusRow;
        }

        ActionSetting? copy = null;
        copy = new ActionSetting(
            "Version details",
            "All of the above as text, ready to paste into a bug report.",
            "Copy",
            () =>
            {
                System.Windows.Clipboard.SetText(SupportText());
                copy!.ButtonText = "Copied";
            });

        return
        [
            new SettingsGroup("OPENOVERLAY", "A free, open-source telemetry overlay for iRacing.")
                .With(
                    new InfoSetting("Version", VersionHint(), BuildInfo.Version),
                    new InfoSetting(
                        "Build",
                        BuildInfo.Commit is null
                            ? "This build doesn't record the commit it was made from."
                            : "The commit this copy was built from. Quote it when reporting a problem.",
                        BuildInfo.Commit ?? "Not recorded"),
                    new InfoSetting("Released", null, ReleaseCatalog.Current?.Date is { } released ? FormatDate(released) : "Unknown"),
                    new InfoSetting(
                        "Installed",
                        InstallHistory.PreviousVersion is { } previous ? $"Previously {previous} on this PC." : "When this version first ran on this PC.",
                        InstallHistory.InstalledUtc is { } installed ? FormatDate(installed) : "Unknown"),
                    new InfoSetting("Update channel", ChannelHint(), BuildInfo.Channel.ToString()),
                    updateStatusRow,
                    copy),
            new SettingsGroup("RELEASE NOTES").With(
                new ActionSetting("What's new", "The changes in the version you're running.", "Open", ShowWhatsNew),
                new ActionSetting("Changelog", "Every release so far, newest first.", "Open", () => Navigate(ChangelogPageKey))),
            new SettingsGroup("HELP AND COMMUNITY").With(
                new ActionSetting("GitHub", "Source code, releases and the issue tracker.", "Open", () => ProjectLinks.Open(ProjectLinks.Repository)),
                new ActionSetting("Documentation", "Setup, features and troubleshooting.", "Open", () => ProjectLinks.Open(ProjectLinks.Documentation)),
                new ActionSetting(
                    "Report a problem",
                    "Opens a new GitHub issue. Attach the report from General › Diagnostics.",
                    "Open",
                    () => ProjectLinks.Open(ProjectLinks.NewIssue)),
                ProjectLinks.Discord is { } discord
                    ? new ActionSetting("Discord", "Talk to other drivers and the developers.", "Open", () => ProjectLinks.Open(discord))
                    : Planned("Discord", "A community server for questions and setups is planned.")),
            new SettingsGroup("SUPPORT TOOLS").With(
                new ActionSetting("Diagnostics", "Copy or export a report of this run, with its logs.", "Open", () => Navigate(GeneralPageKey)),
                Planned("Check for updates now", "Installed copies already check at every start; checking on demand is planned."),
                Planned("Send feedback", "Sending feedback from inside the app is planned; GitHub works today.")),
            new SettingsGroup("LICENSE").With(
                new ActionSetting(
                    "MIT License",
                    "Free to use, change and share. Built with YamlDotNet and Velopack, both MIT licensed.",
                    "View",
                    () => ProjectLinks.Open(ProjectLinks.License))),
        ];
    }

    /// <summary>A support tool that doesn't exist yet, listed so its place is already known.</summary>
    private static ActionSetting Planned(string label, string hint) =>
        new(label, hint, "Coming soon", () => { }, isEnabled: false);

    private static string? VersionHint() => BuildInfo.Release is { IsPrerelease: true } ? "A preview release." : null;

    private static string ChannelHint() => BuildInfo.Channel switch
    {
        UpdateChannel.Stable => "Looks for a new release at every start and installs it the next time OpenOverlay starts.",
        UpdateChannel.Preview => "A pre-release. Updates arrive from GitHub the same way as for stable releases.",
        _ => "A copy that doesn't update itself. The installer on GitHub gives you automatic updates.",
    };

    private string SupportText()
    {
        var environment = AppInfo.Describe();
        var lines = new List<string>
        {
            $"OpenOverlay {BuildInfo.Version}",
            $"Build: {BuildInfo.Commit ?? "not recorded"}",
            $"Channel: {BuildInfo.Channel} ({AppInfo.InstallKind})",
            $"Installed: {(InstallHistory.InstalledUtc is { } installed ? FormatDate(installed) : "unknown")}" +
                (InstallHistory.PreviousVersion is { } previous ? $", previously {previous}" : ""),
            $"Updates: {_updateStatus}",
            $"System: {environment["os"]}, {environment["runtime"]} ({environment["arch"]})",
        };
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDate(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
