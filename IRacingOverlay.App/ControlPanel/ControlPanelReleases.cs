using System.Globalization;
using System.Windows.Input;
using IRacingOverlay.App.About;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Help, at the foot of the rail: one page with three tabs — What's new, Changelog and About — and
/// the version in the status bar, which opens it. What they show comes from three places: the changelog this build
/// embeds (<see cref="ReleaseCatalog"/>), the build's own stamp (<see cref="BuildInfo"/>) and this
/// PC's install history (<see cref="InstallHistory"/>).
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private const string HelpPageKey = "app.help";
    private const int WhatsNewTab = 0;
    private const int ChangelogTab = 1;
    private const int AboutTab = 2;

    private string _updateStatus = "Not checked yet";
    private InfoSetting? _updateStatusRow;
    private RelayCommand? _showAbout;

    /// <summary>The status bar's version: always on screen, one click from the About page.</summary>
    public string VersionLabel => $"v{BuildInfo.Version}";

    public string VersionToolTip => $"{BuildInfo.Summary}\nClick for version details, what's new and the changelog.";

    /// <summary>Under the logo in the side panel of the Help page.</summary>
    public string VersionHeadline => $"Version {BuildInfo.Version} · {BuildInfo.Channel}";

    public ICommand ShowAboutCommand => _showAbout ??= new RelayCommand(() =>
    {
        AppLog.Activity("Control Panel", "Version in the status bar clicked");
        Navigate(HelpPageKey, AboutTab);
    });

    private RelayCommand? _showHelp;

    /// <summary>The Help entry at the foot of the rail.</summary>
    public ICommand ShowHelpCommand => _showHelp ??= new RelayCommand(() => Navigate(HelpPageKey, _selected?.Key == HelpPageKey ? _selectedTab : WhatsNewTab));

    /// <summary>Help and Home: nothing to preview, so the side panel shows the product instead.</summary>
    public bool IsInfoPage => _selected?.Key is HelpPageKey or HomePageKey;

    /// <summary>The Help page itself, for the rail's foot to light up.</summary>
    public bool IsHelpPage => _selected?.Key == HelpPageKey;

    public bool ShowsPreviewPlaceholder => !HasPreview && !IsInfoPage && !HasLayoutPreview;

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
    public void ShowWhatsNew() => Navigate(HelpPageKey, WhatsNewTab);

    private void AddHelpPage()
    {
        NavItems.Add(NavItem.ForFooter(
            HelpPageKey, "Help", "What's new, the changelog, version details and where to get help.",
            "M12,3 A9,9 0 1 0 12.01,3 Z M12,11 V16.5 M12,7.5 V7.51",
            ["What's new", "Changelog", "About"]));
    }

    /// <summary>Opens a page, on one of its tabs.</summary>
    private void Navigate(string key, int tab = 0)
    {
        ClearSearch();
        _selectedTab = tab;
        var page = NavItems.First(item => item.Key == key);
        if (ReferenceEquals(_selected, page))
        {
            BuildSettings(page);
            OnPropertyChanged(nameof(SelectedTab));
        }
        else
        {
            Selected = page;
        }
    }

    private IEnumerable<SettingsGroup> HelpPage() => _selectedTab switch
    {
        ChangelogTab => ChangelogPage(),
        AboutTab => AboutPage(),
        _ => WhatsNewPage(),
    };

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
                    "No release notes in this build. See GitHub."),
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
            new ActionSetting("Full changelog", "Every release so far, newest first.", "Open", () => Navigate(HelpPageKey, ChangelogTab)),
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
                ? "No release notes in this build. See GitHub."
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
        var updateStatusRow = new InfoSetting("Updates", null, _updateStatus);
        if (!_buildingIndex)
        {
            _updateStatusRow = updateStatusRow;
        }

        ActionSetting? copy = null;
        copy = new ActionSetting(
            "Version details",
            "For a bug report.",
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
                    new InfoSetting("Build", null, BuildInfo.Commit ?? "Not recorded"),
                    new InfoSetting("Released", null, ReleaseCatalog.Current?.Date is { } released ? FormatDate(released) : "Unknown"),
                    new InfoSetting(
                        "Installed",
                        InstallHistory.PreviousVersion is { } previous ? $"Previously {previous}." : null,
                        InstallHistory.InstalledUtc is { } installed ? FormatDate(installed) : "Unknown"),
                    new InfoSetting("Update channel", ChannelHint(), BuildInfo.Channel.ToString()),
                    updateStatusRow,
                    copy),
            new SettingsGroup("HELP AND COMMUNITY").With([
                new ActionSetting("GitHub", "Source code, releases and the issue tracker.", "Open", () => ProjectLinks.Open(ProjectLinks.Repository)),
                new ActionSetting("Documentation", "Setup, features and troubleshooting.", "Open", () => ProjectLinks.Open(ProjectLinks.Documentation)),
                new ActionSetting(
                    "Report a problem",
                    "Attach the report from Settings › Diagnostics.",
                    "Open",
                    () => ProjectLinks.Open(ProjectLinks.NewIssue)),
                .. Discord()]),
            new SettingsGroup("SUPPORT TOOLS").With(
                new ActionSetting("Diagnostics", "Report and logs for this run.", "Open", () => Navigate(DiagnosticsPageKey))),
            new SettingsGroup("LICENSE").With(
                new ActionSetting(
                    "MIT License",
                    "Free to use, change and share.",
                    "View",
                    () => ProjectLinks.Open(ProjectLinks.License))),
        ];
    }

    /// <summary>The community link, only once there is one: a button that can't be pressed is a
    /// promise, not an option.</summary>
    private static IEnumerable<SettingItem> Discord() => ProjectLinks.Discord is { } discord
        ? [new ActionSetting("Discord", "Talk to other drivers and the developers.", "Open", () => ProjectLinks.Open(discord))]
        : [];

    private static string? VersionHint() => BuildInfo.Release is { IsPrerelease: true } ? "A preview release." : null;

    private static string ChannelHint() => BuildInfo.Channel switch
    {
        UpdateChannel.Stable => "Checks at every start; updates install on the next start.",
        UpdateChannel.Preview => "Updates install automatically.",
        _ => "No automatic updates. Use the installer to get them.",
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
