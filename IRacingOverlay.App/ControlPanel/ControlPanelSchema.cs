using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Layouts;
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
/// Every page ends with the same POSITION &amp; VISIBILITY group (and, where a widget has one, an
/// ADVANCED note after it). Consistency across widgets was an explicit goal: wherever you are, size
/// and auto-hide are in the same place, and they look the same.
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
        PedalTraceOptions,
        SaveToStores: true,
        TableHeaderChanged: table => TableHeaderChanged?.Invoke(table));

    // Set while pages are built only to be searched: their settings are read and thrown away, so
    // nothing that wires a live setting up (hotkey rows, the dashboard button) may happen then.
    private bool _buildingIndex;

    private void BuildSettings(NavItem item)
    {
        item.Settings.Clear();
        foreach (var group in BuildGroups(item))
        {
            group.Scope = item.Key;
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
                LayoutsPageKey => LayoutsPage(),
                GeneralPageKey => GeneralPage(),
                PerformancePageKey => PerformancePage(),
                HotkeysPageKey => HotkeyGroups(),
                DiagnosticsPageKey => [DiagnosticsGroup()],
                HelpPageKey => HelpPage(),
                _ => [],
            };
        }

        return WidgetPage(slot);
    }

    /// <summary>
    /// Every widget page in the same order, whatever the widget: CONTENT (what it shows), APPEARANCE
    /// (how it looks), BEHAVIOR (how it decides), POSITION &amp; VISIBILITY, then ADVANCED, folded.
    /// A section with nothing in it for this widget is simply not there. Once learnt on one widget,
    /// the order holds on all twelve.
    /// </summary>
    private IEnumerable<SettingsGroup> WidgetPage(WidgetSlot slot)
    {
        // The widget's own settings come from WidgetSettings, shared with the layout editor; this
        // page adds what belongs to the individual widget alone around them.
        var own = WidgetSettings.For(slot.Key, IndividualSettings);
        var advanced = own.FirstOrDefault(group => group.Title == WidgetSettings.Advanced)
            ?? new SettingsGroup(WidgetSettings.Advanced) { CollapsedByDefault = true };
        if (slot.Key is WidgetCatalog.Cockpit or WidgetCatalog.PedalTrace)
        {
            advanced.Items.Add(RefreshRateLink());
        }

        List<SettingsGroup> groups = [];
        // While a layout controls this widget, the page says so before anything else.
        if (LayoutNotice(slot.Key) is { } notice)
        {
            groups.Add(notice);
        }

        if (slot.Key == WidgetCatalog.Flag)
        {
            groups.Add(FlagPreviewGroup());
        }

        groups.AddRange(own.Where(group => group.Title != WidgetSettings.Advanced));
        groups.Add(Placement(slot));
        if (advanced.Items.Count > 0)
        {
            groups.Add(advanced);
        }

        return groups;
    }

    // ===== Shared =====

    /// <summary>Size and auto-hide: the two things every widget has, laid out identically on every
    /// page so they can be changed without reading anything.</summary>
    private SettingsGroup Placement(WidgetSlot slot) => new SettingsGroup(
        WidgetSettings.PositionAndVisibility,
        "Size, transparency and when it shows. To move it, use Move widgets in the toolbar.")
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

    private RelayCommand? _resetWidget;
    private bool _isWidgetMenuOpen;

    /// <summary>The ⋯ menu in a widget page's header.</summary>
    public bool IsWidgetMenuOpen
    {
        get => _isWidgetMenuOpen;
        set
        {
            if (_isWidgetMenuOpen == value)
            {
                return;
            }

            _isWidgetMenuOpen = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Puts the open widget back as it came: its options, size, opacity and auto-hide. Where
    /// it sits on screen stays — that has its own reset, for every widget at once, on General.</summary>
    public ICommand ResetWidgetCommand => _resetWidget ??= new RelayCommand(() =>
    {
        IsWidgetMenuOpen = false;
        if (SelectedWidget is not { } slot)
        {
            return;
        }

        var answer = LayoutDialog.Ask(
            DialogOwner,
            $"Reset {slot.Descriptor.Name} to defaults?",
            "Its options, size, opacity and \"Hide when I'm not driving\" go back to how they came. Where it sits on screen doesn't change.",
            ["Reset", "Cancel"],
            primary: 1);
        if (answer != 0)
        {
            return;
        }

        AppLog.Activity("Control Panel", $"{slot.Descriptor.Name} reset to defaults");
        CreateConfigCodecs()[slot.Key].Apply(WidgetDefaults.ConfigOf(slot.Key));
        slot.Scale = ScaleLevels.Default;
        slot.Opacity = WidgetOpacityStore.Default;
        slot.HideOutsideCar = false;
        if (_selected is { } page)
        {
            BuildSettings(page);
        }
    });

    /// <summary>The fast displays' refresh rate is one setting for all of them, on the Performance
    /// page; the widgets that use it point there rather than offering a second copy of it.</summary>
    private ActionSetting RefreshRateLink() => new(
        "Refresh rate",
        "Shared by the proximity bars, ABS light and pedal trace. Set in Settings › Performance.",
        "Open Performance",
        () => Navigate(PerformancePageKey));

    // ===== Flags =====

    private SettingsGroup FlagPreviewGroup() => new SettingsGroup("PREVIEW")
        .With(new ChoiceSetting(
            "Simulate",
            "Preview any flag without iRacing.",
            PreviewData.FlagScenarios.Select(s => s.Label).ToList(),
            FlagPreview.Index,
            index => FlagPreview.Index = index));

    // ===== Application pages =====

    /// <summary>The application as a whole: its window, its units, and the overlays all at once.
    /// Performance, hotkeys and diagnostics have their own pages beside it.</summary>
    private IEnumerable<SettingsGroup> GeneralPage() =>
    [
        new SettingsGroup(
            "WINDOW",
            "In the tray, overlays keep running. Quit from the tray menu.")
            .With(
                new ChoiceSetting(
                    "When I close this window",
                    null,
                    ["Keep overlays running in tray", "Quit OpenOverlay"],
                    (int)TrayPreferencesStore.CloseBehavior,
                    index => TrayPreferencesStore.SaveCloseBehavior((CloseBehavior)index)),
                new ActionSetting(
                    "Configuration folder",
                    "Layouts, options and hotkeys.",
                    "Open folder",
                    OpenConfigFolder)),
        .. UnitsGroups(),
        new SettingsGroup("OVERLAYS", "Every widget at once.")
            .With(
                new ActionSetting(
                    "Reset positions",
                    "Moves every widget back to its default spot. Sizes and options stay.",
                    "Reset positions…",
                    ConfirmResetPositions) { IsDestructive = true },
                new ActionSetting(
                    "Restart overlays",
                    "Closes and reopens every widget window, which clears a rendering glitch.",
                    "Restart",
                    RestartOverlays)),
    ];

    private void ConfirmResetPositions()
    {
        var answer = LayoutDialog.Ask(
            DialogOwner,
            "Reset every widget's position?",
            "Every widget goes back to its default spot on screen, including ones that are off. Sizes and options don't change.",
            ["Reset positions", "Cancel"],
            primary: 1);
        if (answer == 0)
        {
            ResetLayout();
        }
    }

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
            "THIS RUN",
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

    private IEnumerable<SettingsGroup> PerformancePage() =>
    [
        new SettingsGroup(
            "PERFORMANCE",
            "How often the fast displays redraw: proximity bars, ABS light and pedal trace.")
            .With(new ChoiceSetting(
                "Refresh rate",
                "Faster is a smoother pedal trace and radar, for slightly more CPU.",
                ["Smoothest (~60 Hz)", "Smooth (30 Hz)", "Normal (15 Hz)", "Light (10 Hz)", "Lightest (5 Hz)"],
                _criticalRefreshIndex,
                index =>
                {
                    _criticalRefreshIndex = index;
                    CriticalRefreshStore.Save(index);
                    CriticalRefreshChanged?.Invoke(CriticalRefreshIntervalMs);
                })),
    ];
}
