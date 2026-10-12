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

    private string _updateStatus = Loc.T("update.notChecked");
    private InfoSetting? _updateStatusRow;
    private RelayCommand? _showAbout;

    /// <summary>The status bar's version: always on screen, one click from the About page.</summary>
    public string VersionLabel => $"v{BuildInfo.Version}";

    public string VersionToolTip => $"{BuildInfo.Summary}\n{Loc.T("help.versionTip")}";


    public ICommand ShowAboutCommand => _showAbout ??= new RelayCommand(() =>
    {
        AppLog.Activity("Control Panel", "Version in the status bar clicked");
        Navigate(HelpPageKey, AboutTab);
    });

    private RelayCommand? _showHelp;

    /// <summary>The Help entry at the foot of the rail.</summary>
    public ICommand ShowHelpCommand => _showHelp ??= new RelayCommand(() => Navigate(HelpPageKey, _selected?.Key == HelpPageKey ? _selectedTab : WhatsNewTab));

    /// <summary>Home: the side panel shows what is on screen right now.</summary>
    public bool IsHomePage => _selected?.Key == HomePageKey;

    /// <summary>The Help page itself, for the rail's foot to light up.</summary>
    public bool IsHelpPage => _selected?.Key == HelpPageKey;

    /// <summary>A page with nothing for the side panel (the settings pages, Help): the panel goes
    /// and the page takes the width.</summary>
    public bool ShowsPreviewPlaceholder => !HasPreview && !HasLayoutPreview && !IsHomePage;

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
            HelpPageKey, "nav.help.title", "nav.help.blurb",
            "M12,3 A9,9 0 1 0 12.01,3 Z M12,11 V16.5 M12,7.5 V7.51",
            ["nav.help.tab.whatsNew", "nav.help.tab.changelog", "nav.help.tab.about"]));
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
                    Loc.F("help.versionHeading", BuildInfo.Version),
                    Loc.T("help.noReleaseNotesIn")),
                ReleaseLinks(null),
            ];
        }

        var groups = new List<SettingsGroup> { new(Loc.F("help.whatsNewIn", entry.Version), WhatsNewSubtitle(entry)) };
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
            parts.Add(Loc.F("help.released", FormatDate(released)));
        }

        if (InstallHistory.InstalledUtc is { } installed)
        {
            parts.Add(Loc.F("help.installedSince", FormatDate(installed)));
        }

        if (entry.Summary is { } summary)
        {
            parts.Add(summary);
        }

        return string.Join(' ', parts);
    }

    private static string SectionTitle(ChangelogSection section) =>
        section.Title.Length > 0 ? section.Title.ToUpperInvariant() : Loc.T("updated.changes");

    private SettingsGroup ReleaseLinks(ChangelogEntry? entry) => new SettingsGroup(Loc.T("help.more"))
        .With(
            new ActionSetting(Loc.T("help.fullChangelog"), Loc.T("help.everyReleaseSoFar"), Loc.T("common.open"), () => Navigate(HelpPageKey, ChangelogTab)),
            entry is not null
                ? new ActionSetting(Loc.T("help.releasePage"), Loc.F("help.releasePage.hint", entry.Version), Loc.T("common.open"), () => ProjectLinks.Open(ProjectLinks.Release(entry.Version)))
                : new ActionSetting(Loc.T("help.releases"), Loc.T("help.everyReleaseOnGithub"), Loc.T("common.open"), () => ProjectLinks.Open(ProjectLinks.Releases)));

    // ===== Changelog =====

    private IEnumerable<SettingsGroup> ChangelogPage()
    {
        var entries = ReleaseCatalog.Changelog.Entries;
        var releases = new SettingsGroup(
            Loc.T("help.releases2"),
            entries.Count == 0
                ? Loc.T("help.noReleaseNotesIn")
                : Loc.T("help.newestFirstSelectA"));

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            releases.Items.Add(new ReleaseSetting(
                $"v{entry.Version}",
                entry.Date is { } date ? FormatDate(date) : null,
                i == 0 ? Loc.T("help.yourVersion") : null,
                entry.Summary,
                entry.Sections.Select(section => new NoteSection(SectionTitle(section), section.Items, section.IsWarning)).ToList(),
                isExpanded: i == 0));
        }

        return
        [
            releases,
            new SettingsGroup(Loc.T("help.more")).With(
                new ActionSetting(Loc.T("help.changelogOnGithub"), Loc.T("help.theSameHistoryAs"), Loc.T("common.open"), () => ProjectLinks.Open(ProjectLinks.Changelog)),
                new ActionSetting(Loc.T("help.releases"), Loc.T("help.everyReleaseOnGithub"), Loc.T("common.open"), () => ProjectLinks.Open(ProjectLinks.Releases))),
        ];
    }

    // ===== About =====

    private IEnumerable<SettingsGroup> AboutPage()
    {
        var updateStatusRow = new InfoSetting(Loc.T("help.updates"), null, _updateStatus);
        if (!_buildingIndex)
        {
            _updateStatusRow = updateStatusRow;
        }

        ActionSetting? copy = null;
        copy = new ActionSetting(
            Loc.T("help.versionDetails"),
            Loc.T("help.forABugReport"),
            Loc.T("common.copy"),
            () =>
            {
                System.Windows.Clipboard.SetText(SupportText());
                copy!.ButtonText = Loc.T("common.copied");
            });

        return
        [
            new SettingsGroup("OPENOVERLAY", Loc.T("help.aFreeOpenSource"))
                .With(
                    new InfoSetting(Loc.T("help.version"), VersionHint(), BuildInfo.Version),
                    new InfoSetting(Loc.T("help.build"), null, BuildInfo.Commit ?? Loc.T("help.notRecorded")),
                    new InfoSetting(Loc.T("help.released2"), null, ReleaseCatalog.Current?.Date is { } released ? FormatDate(released) : Loc.T("help.unknown")),
                    new InfoSetting(
                        Loc.T("help.installed"),
                        InstallHistory.PreviousVersion is { } previous ? Loc.F("help.previously", previous) : null,
                        InstallHistory.InstalledUtc is { } installed ? FormatDate(installed) : Loc.T("help.unknown")),
                    new InfoSetting(Loc.T("help.updateChannel"), ChannelHint(), BuildInfo.Channel.ToString()),
                    updateStatusRow,
                    copy),
            new SettingsGroup(Loc.T("help.helpAndCommunity")).With([
                new ActionSetting("GitHub", Loc.T("help.sourceCodeReleasesAnd"), Loc.T("common.open"), () => ProjectLinks.Open(ProjectLinks.Repository)),
                new ActionSetting(Loc.T("help.documentation"), Loc.T("help.setupFeaturesAndTroubleshooting"), Loc.T("common.open"), () => ProjectLinks.Open(ProjectLinks.Documentation)),
                new ActionSetting(
                    Loc.T("help.reportAProblem"),
                    Loc.T("help.attachTheReportFrom"),
                    Loc.T("common.open"),
                    () => ProjectLinks.Open(ProjectLinks.NewIssue)),
                .. Discord()]),
            new SettingsGroup(Loc.T("help.supportTools")).With(
                new ActionSetting(Loc.T("nav.diagnostics.title"), Loc.T("help.reportAndLogsFor"), Loc.T("common.open"), () => Navigate(DiagnosticsPageKey))),
            new SettingsGroup(Loc.T("help.license")).With(
                new ActionSetting(
                    Loc.T("help.mitLicense"),
                    Loc.T("help.freeToUseChange"),
                    Loc.T("help.view"),
                    () => ProjectLinks.Open(ProjectLinks.License))),
        ];
    }

    /// <summary>The community link, only once there is one: a button that can't be pressed is a
    /// promise, not an option.</summary>
    private static IEnumerable<SettingItem> Discord() => ProjectLinks.Discord is { } discord
        ? [new ActionSetting("Discord", Loc.T("help.talkToOtherDrivers"), Loc.T("common.open"), () => ProjectLinks.Open(discord))]
        : [];

    private static string? VersionHint() => BuildInfo.Release is { IsPrerelease: true } ? Loc.T("help.aPreviewRelease") : null;

    private static string ChannelHint() => BuildInfo.Channel switch
    {
        UpdateChannel.Stable => Loc.T("help.checksAtEveryStart"),
        UpdateChannel.Preview => Loc.T("help.updatesInstallAutomatically"),
        _ => Loc.T("help.noAutomaticUpdatesUse"),
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
