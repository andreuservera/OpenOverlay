using System.Diagnostics;
using System.Globalization;
using System.IO;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// What each page offers, in one file.
///
/// This is the extension point the rest of the design exists to protect. Adding an option to a
/// widget is a single line here; adding a widget is an entry in <see cref="WidgetCatalog"/>, a
/// factory case, and — only if it has settings of its own — one case below. No XAML, no new handler,
/// no new field, and nothing to remember to add to a loop somewhere else.
///
/// Every page ends with the same PLACEMENT group. Consistency across widgets was an explicit goal:
/// wherever you are, size and auto-hide are in the same place, and they look the same.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private static readonly string[] SizeLadder = ScaleLevels.Labels;

    /// <summary>The widget pages' settings act on the shared live options and save as they always
    /// have, header nudges included.</summary>
    private WidgetSettingsContext IndividualSettings => new(
        StandingsOptions,
        RelativeOptions,
        FuelCalculatorOptions,
        FlagOptions,
        CockpitOptions,
        WeatherOptions,
        TrackInfoOptions,
        DeltaOptions,
        SaveToStores: true,
        TableHeaderChanged: table => TableHeaderChanged?.Invoke(table));

    // Set while pages are built only to be searched: their settings are read and thrown away, so
    // nothing that wires a live setting up (hotkey rows, the dashboard button) may happen then.
    private bool _buildingIndex;

    private void BuildSettings(NavItem item)
    {
        item.Settings.Clear();

        var groups = BuildGroups(item).ToList();
        var term = _searchText.Trim();
        if (term.Length > 0 && !Contains(item.Title, term))
        {
            // Searching: the page shows just the groups that matched, whole, so each keeps its context.
            // A page found by its name shows everything; one found by its blurb still narrows.
            var matching = groups.Where(group => SearchTermsOf(group).Any(text => Contains(text, term))).ToList();
            if (matching.Count > 0)
            {
                groups = matching;
            }
        }

        foreach (var group in groups)
        {
            // Names every change in the activity trail after where the user made it.
            var path = $"{item.Title} › {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(group.Title.ToLowerInvariant())} › ";
            foreach (var setting in group.Items)
            {
                setting.TracePath = path;
                if (setting is ChipGroupSetting chips)
                {
                    foreach (var chip in chips.Chips)
                    {
                        chip.TracePath = $"{path}{chips.Label} › ";
                    }
                }
                else if (setting is ReorderListSetting columns)
                {
                    foreach (var chip in columns.Items.SelectMany(column => new[] { column.Visible, column.Companion }).OfType<ChipSetting>())
                    {
                        chip.TracePath = $"{path}{columns.Label} › ";
                    }
                }
            }

            item.Settings.Add(group);
        }
    }

    private IEnumerable<SettingsGroup> BuildGroups(NavItem item)
    {
        if (item.Widget is not { } slot)
        {
            return item.Key switch
            {
                DashboardPageKey => DashboardPage(),
                LayoutsPageKey => LayoutsPage(),
                GeneralPageKey => GeneralPage(),
                WhatsNewPageKey => WhatsNewPage(),
                ChangelogPageKey => ChangelogPage(),
                AboutPageKey => AboutPage(),
                _ => [],
            };
        }

        // The widget's own settings come from WidgetSettings, shared with the layout editor; this
        // page adds what belongs to the individual widget alone around them.
        var own = WidgetSettings.For(slot.Key, IndividualSettings);
        IEnumerable<SettingsGroup> groups = slot.Key switch
        {
            WidgetCatalog.Flag => [FlagPreviewGroup(), .. own, Placement(slot)],
            _ => [.. own, Placement(slot)],
        };

        // While a layout controls this widget, the page says so before anything else.
        return LayoutNotice(slot.Key) is { } notice ? [notice, .. groups] : groups;
    }

    // ===== Search =====

    private bool Matches(NavItem item, string term) =>
        Contains(item.Title, term) ||
        Contains(item.Blurb, term) ||
        (_searchIndex?.TryGetValue(item, out var text) == true && Contains(text, term));

    private Dictionary<NavItem, string> BuildSearchIndex()
    {
        _buildingIndex = true;
        try
        {
            return NavItems.ToDictionary(item => item, item => string.Join('\n', BuildGroups(item).SelectMany(SearchTermsOf)));
        }
        finally
        {
            _buildingIndex = false;
        }
    }

    private static IEnumerable<string> SearchTermsOf(SettingsGroup group)
    {
        yield return group.Title;
        if (group.Subtitle is { } subtitle)
        {
            yield return subtitle;
        }

        foreach (var item in group.Items)
        {
            // Release notes are prose about the app, not options in it.
            if (item is BulletSetting or ReleaseSetting)
            {
                continue;
            }

            yield return item.Label;
            if (item.Hint is { } hint)
            {
                yield return hint;
            }

            IEnumerable<string> extra = item switch
            {
                ChoiceSetting choice => choice.Options,
                SegmentedSetting segmented => segmented.Options,
                ChipGroupSetting chips => chips.Chips.SelectMany(chip => new[] { chip.Label, chip.Hint ?? "" }),
                ReorderListSetting columns => columns.Items.Select(column => column.Label),
                HotkeySetting hotkey => [hotkey.Display],
                _ => [],
            };
            foreach (var text in extra)
            {
                yield return text;
            }
        }
    }

    private static bool Contains(string text, string term) => text.Contains(term, StringComparison.OrdinalIgnoreCase);

    // ===== Shared =====

    /// <summary>Size and auto-hide: the two things every widget has, laid out identically on every
    /// page so they can be changed without reading anything.</summary>
    private SettingsGroup Placement(WidgetSlot slot) => new SettingsGroup(
        "PLACEMENT",
        "Unlock widgets from the toolbar, then drag the widget to move it.")
        .With(
            new SegmentedSetting(
                "Size",
                null,
                SizeLadder,
                (int)slot.Scale,
                index => slot.Scale = (ScaleLevel)index),
            new SliderSetting(
                "Opacity",
                null,
                slot.Opacity,
                0,
                1,
                0.01,
                value => slot.Opacity = value),
            new ToggleSetting(
                "Hide when I'm not driving",
                "Hidden in menus, the garage, replays and while spectating.",
                slot.HideOutsideCar,
                value => slot.HideOutsideCar = value));

    // ===== Flags =====

    private SettingsGroup FlagPreviewGroup() => new SettingsGroup("PREVIEW")
        .With(new ChoiceSetting(
            "Simulate",
            "Preview any flag without iRacing.",
            PreviewData.FlagScenarios.Select(s => s.Label).ToList(),
            FlagPreview.Index,
            index => FlagPreview.Index = index));

    // ===== Application pages =====

    /// <summary>Everything about the application rather than one widget, on one page: the window,
    /// units, performance, hotkeys and diagnostics.</summary>
    private IEnumerable<SettingsGroup> GeneralPage() =>
    [
        new SettingsGroup(
            "WINDOW",
            "In the tray, overlays keep running. Exit from the tray menu.")
            .With(
                new ChoiceSetting(
                    "Close button behavior",
                    null,
                    ["Minimize to system tray (recommended)", "Exit application"],
                    (int)TrayPreferencesStore.CloseBehavior,
                    index => TrayPreferencesStore.SaveCloseBehavior((CloseBehavior)index)),
                new ActionSetting(
                    "Configuration folder",
                    "Layouts, options and hotkeys.",
                    "Open folder",
                    OpenConfigFolder)),
        .. UnitsGroups(),
        .. PerformanceGroups(),
        .. HotkeyGroups(),
        DiagnosticsGroup(),
    ];

    /// <summary>Everything a bug report needs, one click away. The report masks the Windows user
    /// folder, so it can be pasted into a public issue as is.</summary>
    private static SettingsGroup DiagnosticsGroup()
    {
        ActionSetting? copy = null;
        copy = new ActionSetting(
            "Copy diagnostics",
            null,
            "Copy",
            () =>
            {
                System.Windows.Clipboard.SetText(DiagnosticsReport.Build("Copied by the user"));
                copy!.ButtonText = "Copied";
            });

        var problems = AppLog.Problems();
        var errors = problems.Where(p => p.Level >= LogLevel.Error).Sum(p => p.Count);
        var warnings = problems.Where(p => p.Level == LogLevel.Warning).Sum(p => p.Count);
        return new SettingsGroup(
            "DIAGNOSTICS",
            $"This run: {AppInfo.RunId} · {errors} {(errors == 1 ? "error" : "errors")} · {warnings} {(warnings == 1 ? "warning" : "warnings")}. " +
            "Logs stay on your PC.")
            .With(
                copy,
                new ActionSetting(
                    "Export report",
                    "A .zip with logs, crash reports and settings.",
                    "Export",
                    () => ShowInExplorer(DiagnosticsReport.Export())),
                new ActionSetting(
                    "Log files",
                    "One per day, kept 30 days.",
                    "Open folder",
                    () =>
                    {
                        Directory.CreateDirectory(AppLog.LogDirectory);
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppLog.LogDirectory}\"") { UseShellExecute = true });
                    }));
    }

    private static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    /// <summary>Opens the settings folder in Explorer, creating it first on a fresh install.</summary>
    public static void OpenConfigFolder()
    {
        Directory.CreateDirectory(TrayPreferencesStore.ConfigFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TrayPreferencesStore.ConfigFolder}\"") { UseShellExecute = true });
    }

    private IEnumerable<SettingsGroup> UnitsGroups() =>
    [
        new SettingsGroup("UNITS")
            .With(new SegmentedSetting(
                "Units",
                Units.IRacingSetting is { } iracing
                    ? $"iRacing is set to {(iracing == UnitSystem.Imperial ? "imperial" : "metric")}."
                    : "Metric until iRacing reports its setting.",
                ["Follow iRacing", "Metric", "Imperial"],
                (int)Units.Preference,
                index =>
                {
                    var preference = (UnitPreference)index;
                    UnitPreferenceStore.Save(preference);
                    Units.SetPreference(preference);
                })),
    ];

    private IEnumerable<SettingsGroup> PerformanceGroups() =>
    [
        new SettingsGroup(
            "PERFORMANCE · HIGH-RATE DISPLAYS",
            "Proximity bars, ABS light and pedal trace.")
            .With(new ChoiceSetting(
                "Refresh rate",
                "Faster costs more CPU.",
                ["Fastest (~60 Hz)", "Fast (30 Hz)", "Normal (15 Hz)", "Slow (10 Hz)", "Slowest (5 Hz)"],
                _criticalRefreshIndex,
                index =>
                {
                    _criticalRefreshIndex = index;
                    CriticalRefreshStore.Save(index);
                    CriticalRefreshChanged?.Invoke(CriticalRefreshIntervalMs);
                })),
    ];

    private IEnumerable<SettingsGroup> DashboardPage()
    {
        var button = new ActionSetting(
            "Second-monitor dashboard",
            null,
            "Show dashboard",
            () => DashboardToggleRequested?.Invoke());
        if (!_buildingIndex)
        {
            DashboardButton = button;
        }

        return
        [
            new SettingsGroup("OUTPUT")
                .With(
                    new ChoiceSetting(
                        "Monitor",
                        null,
                        MonitorNames,
                        _selectedMonitorIndex,
                        index => _selectedMonitorIndex = index),
                    new ChoiceSetting(
                        "Theme",
                        "Floating widgets aren't affected.",
                        ["Classic", "Digital HUD", "Raw DIY"],
                        (int)_dashboardTheme,
                        index =>
                        {
                            _dashboardTheme = (DashboardTheme)index;
                            DashboardThemeStore.Save(_dashboardTheme);
                            DashboardThemeChanged?.Invoke(_dashboardTheme);
                        }),
                    button),
        ];
    }
}
