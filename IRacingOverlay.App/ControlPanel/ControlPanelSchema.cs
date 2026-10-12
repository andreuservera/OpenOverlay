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
                HomePageKey => HomePage(),
                LayoutsPageKey => LayoutsPage(),
                GeneralPageKey => GeneralPage(),
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

        groups.AddRange(own.Where(group => group.Title != WidgetSettings.Advanced));
        groups.Add(Placement(slot));
        if (advanced.Items.Count > 0)
        {
            groups.Add(advanced);
        }

        // The same page built from untouched options: each row learns how it came, so a changed one
        // shows it and its section can be reset.
        var defaults = WidgetSettings.For(slot.Key, DefaultSettings)
            .Append(PlacementDefaults())
            .ToDictionary(group => group.Title);
        foreach (var group in groups)
        {
            if (defaults.TryGetValue(group.Title, out var original))
            {
                group.AttachDefaults(original);
            }
        }

        return groups;
    }

    /// <summary>Settings over fresh options, saved nowhere: what every widget page compares with.</summary>
    private static WidgetSettingsContext DefaultSettings => new(
        new DriverTableOptions(DriverTable.Standings),
        new DriverTableOptions(DriverTable.Relative),
        new FuelCalculatorOptions(),
        new FlagOptions(),
        new CockpitOptions(),
        new WeatherOptions(),
        new TrackInfoOptions(),
        new DeltaOptions(),
        new PedalTraceOptions(),
        SaveToStores: false);

    /// <summary>POSITION &amp; VISIBILITY as every widget starts, row for row as <see cref="Placement"/>
    /// builds it.</summary>
    private static SettingsGroup PlacementDefaults() => new SettingsGroup(WidgetSettings.PositionAndVisibility)
        .With(
            new SegmentedSetting("Size", null, SizeLadder, (int)ScaleLevels.Default, _ => { }),
            new SliderSetting("Opacity", null, WidgetOpacityStore.Default, 0, 1, 0.01, _ => { }),
            new ToggleSetting("Hide when I'm not driving", null, false, _ => { }));

    // ===== Shared =====

    /// <summary>Size and auto-hide: the two things every widget has, laid out identically on every
    /// page so they can be changed without reading anything.</summary>
    private SettingsGroup Placement(WidgetSlot slot) => new SettingsGroup(
        WidgetSettings.PositionAndVisibility,
        "Size, transparency and when it shows. To move it, use Move widgets in the toolbar.")
        .With(
            SizeRow(slot),
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
                value => slot.HideOutsideCar = value),
            new ActionSetting(
                "Use on every widget",
                "Gives all twelve this size, opacity and \"Hide when I'm not driving\".",
                "Apply to all",
                () => ApplyPlacementToAll(slot)));

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
            if (value)
            {
                // Read as the menu opens, so it lists the layouts as they are now.
                OnPropertyChanged(nameof(WidgetCopyTargets));
                OnPropertyChanged(nameof(HasWidgetCopyTargets));
            }
        }
    }

    /// <summary>Puts the open widget back as it came: its options, size, opacity and auto-hide. Where
    /// it sits on screen stays — that has its own reset, for every widget at once, on General. Undo
    /// puts back what it had.</summary>
    public ICommand ResetWidgetCommand => _resetWidget ??= new RelayCommand(() =>
    {
        IsWidgetMenuOpen = false;
        if (SelectedWidget is not { } slot)
        {
            return;
        }

        var codec = CreateConfigCodecs()[slot.Key];
        var before = (Config: codec.Read(), slot.Scale, slot.Opacity, slot.HideOutsideCar);
        AppLog.Activity("Control Panel", $"{slot.Descriptor.Name} reset to defaults");
        codec.Apply(WidgetDefaults.ConfigOf(slot.Key));
        slot.Scale = ScaleLevels.Default;
        slot.Opacity = WidgetOpacityStore.Default;
        slot.HideOutsideCar = false;
        RebuildPage();

        ShowToast($"{slot.Descriptor.Name} is back to its defaults.", () =>
        {
            CreateConfigCodecs()[slot.Key].Apply(before.Config);
            slot.Scale = before.Scale;
            slot.Opacity = before.Opacity;
            slot.HideOutsideCar = before.HideOutsideCar;
            RebuildPage();
        });
    });

    /// <summary>The ⋯ menu's "copy to a layout": every saved layout that has this widget in it. The
    /// open one is said to be open — while it is, this widget is showing that layout's settings, so
    /// copying into it keeps the changes made since it opened, which closing it would otherwise undo.</summary>
    public IReadOnlyList<MenuEntry> WidgetCopyTargets => SelectedWidget is not { } slot
        ? []
        : LayoutStore.List()
            .Where(layout => layout.Contains(slot.Key))
            .Select(layout => new MenuEntry(
                layout.Id == LayoutStore.Open?.LayoutId ? $"{layout.Name} · open now, keep these changes" : layout.Name,
                new RelayCommand(() => CopyWidgetToLayout(slot, layout.Id))))
            .ToList();

    public bool HasWidgetCopyTargets => WidgetCopyTargets.Count > 0;

    /// <summary>Gives the layout's copy of this widget the options this widget has now. Size,
    /// opacity and place in the layout are the layout's own, and stay.</summary>
    private void CopyWidgetToLayout(WidgetSlot slot, Guid layoutId)
    {
        IsWidgetMenuOpen = false;
        if (LayoutStore.Get(layoutId) is not { } layout || layout.Widgets.FirstOrDefault(widget => widget.Type == slot.Key) is not { } entry)
        {
            return;
        }

        var before = (System.Text.Json.Nodes.JsonObject)entry.Config.DeepClone();
        entry.Config = CreateConfigCodecs()[slot.Key].Read();
        LayoutStore.Save(layout);
        AppLog.Activity("Layouts", $"{slot.Descriptor.Name} settings copied to \"{layout.Name}\"");
        ReapplyIfOpen(layoutId);
        RefreshLayoutsPage();

        ShowToast($"{slot.Descriptor.Name} settings copied to \"{layout.Name}\".", () =>
        {
            if (LayoutStore.Get(layoutId) is { } current && current.Widgets.FirstOrDefault(widget => widget.Type == slot.Key) is { } restored)
            {
                restored.Config = before;
                LayoutStore.Save(current);
                ReapplyIfOpen(layoutId);
                RefreshLayoutsPage();
            }
        });
    }

    /// <summary>This widget's size, opacity and auto-hide, given to every widget. Undo puts each
    /// one's own back.</summary>
    private void ApplyPlacementToAll(WidgetSlot source)
    {
        var before = _slots.Values.Select(slot => (Slot: slot, slot.Scale, slot.Opacity, slot.HideOutsideCar)).ToList();
        foreach (var slot in _slots.Values)
        {
            slot.Scale = source.Scale;
            slot.Opacity = source.Opacity;
            slot.HideOutsideCar = source.HideOutsideCar;
        }

        AppLog.Activity("Control Panel", $"{source.Descriptor.Name}'s size, opacity and auto-hide applied to every widget");
        ShowToast($"Every widget now has {source.Descriptor.Name}'s size, opacity and auto-hide.", () =>
        {
            foreach (var (slot, scale, opacity, hide) in before)
            {
                slot.Scale = scale;
                slot.Opacity = opacity;
                slot.HideOutsideCar = hide;
            }

            RebuildPage();
        });
    }

    /// <summary>Builds the open page again from current state.</summary>
    private void RebuildPage()
    {
        if (_selected is { } page)
        {
            BuildSettings(page);
        }
    }

    /// <summary>The size ladder, with what the level means in numbers under it.</summary>
    private static SegmentedSetting SizeRow(WidgetSlot slot)
    {
        SegmentedSetting? row = null;
        row = new SegmentedSetting(
            "Size",
            SizeHint(slot.Scale),
            SizeLadder,
            (int)slot.Scale,
            index =>
            {
                slot.Scale = (ScaleLevel)index;
                row!.Hint = SizeHint(slot.Scale);
            });
        return row;
    }

    /// <summary>"XL · 139 % of the standard size": the letters say small or large, the number how much.</summary>
    private static string SizeHint(ScaleLevel level) => string.Create(
        CultureInfo.InvariantCulture,
        $"{ScaleLevels.LabelOf(level)} · {ScaleLevels.FactorOf(level) / ScaleLevels.FactorOf(ScaleLevels.Default) * 100:0} % of the standard size (M).");

    /// <summary>The fast displays' refresh rate is one setting for all of them, on the General
    /// page; the widgets that use it point there rather than offering a second copy of it.</summary>
    private ActionSetting RefreshRateLink() => new(
        "Refresh rate",
        "Shared by the proximity bars, ABS light and pedal trace. Set in Settings › General › Performance.",
        "Open setting",
        () =>
        {
            Navigate(GeneralPageKey);
            RevealSetting(PerformanceGroupTitle, RefreshRateLabel);
        });

    // ===== Application pages =====

    /// <summary>The application as a whole: its window, its units, how hard it works, and the
    /// overlays all at once. Hotkeys and diagnostics have their own pages beside it.</summary>
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
                new SegmentedSetting(
                    "Interface size",
                    "How big this window draws its text and controls. The widgets have their own sizes.",
                    InterfaceScaleStore.Steps.Select(step => string.Create(CultureInfo.InvariantCulture, $"{step * 100:0} %")).ToList(),
                    Array.IndexOf(InterfaceScaleStore.Steps, InterfaceScale),
                    index => InterfaceScale = InterfaceScaleStore.Steps[index]),
                new ActionSetting(
                    "Configuration folder",
                    "Layouts, options and hotkeys.",
                    "Open folder",
                    OpenConfigFolder)),
        .. UnitsGroups(),
        PerformanceGroup(),
        new SettingsGroup("OVERLAYS", "Every widget at once.")
            .With(
                new ActionSetting(
                    "Reset positions",
                    "Moves every widget back to its default spot. Sizes and options stay.",
                    "Reset positions",
                    ResetPositionsWithUndo) { IsDestructive = true },
                new ActionSetting(
                    "Restart overlays",
                    "Closes and reopens every widget window, which clears a rendering glitch.",
                    "Restart",
                    RestartOverlays)),
    ];

    /// <summary>Every widget back to its default spot; undo puts each back where it was.</summary>
    private void ResetPositionsWithUndo()
    {
        var before = _slots.Values.Select(slot => (Slot: slot, Position: slot.SavedPosition)).ToList();
        ResetLayout();
        ShowToast("Every widget is back in its default spot.", () =>
        {
            foreach (var (slot, position) in before)
            {
                if (position is { } at)
                {
                    slot.MoveTo(at.Left, at.Top);
                }
                else
                {
                    slot.ForgetPosition();
                }
            }
        });
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

    private const string PerformanceGroupTitle = "PERFORMANCE";
    private const string RefreshRateLabel = "Refresh rate";

    private SettingsGroup PerformanceGroup() =>
        new SettingsGroup(
            PerformanceGroupTitle,
            "How often the fast displays redraw: proximity bars, ABS light and pedal trace.")
            .With(new ChoiceSetting(
                RefreshRateLabel,
                "Faster is a smoother pedal trace and radar, for slightly more CPU.",
                ["Smoothest (~60 Hz)", "Smooth (30 Hz)", "Normal (15 Hz)", "Light (10 Hz)", "Lightest (5 Hz)"],
                _criticalRefreshIndex,
                index =>
                {
                    _criticalRefreshIndex = index;
                    CriticalRefreshStore.Save(index);
                    CriticalRefreshChanged?.Invoke(CriticalRefreshIntervalMs);
                }));
}

/// <summary>An entry in a ⋯ menu.</summary>
public sealed record MenuEntry(string Label, ICommand Command);
