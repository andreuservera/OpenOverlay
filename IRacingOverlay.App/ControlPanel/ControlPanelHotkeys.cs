using System.Windows;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Global hotkeys: the saved bindings, the actions they trigger, and the Hotkeys page. Registration with Windows lives in MainWindow's <see cref="GlobalHotkeyManager"/>; this side
/// only says what the bindings are and reports back which ones Windows refused.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private static string HotkeyTakenMessage => Loc.T("hotkeys.taken");

    private List<HotkeyBinding> _hotkeys = HotkeyStore.Load(AllHotkeyActions()).ToList();
    private readonly Dictionary<string, HotkeySetting> _hotkeySettings = [];
    private IReadOnlySet<string> _hotkeyFailures = new HashSet<string>();
    private bool _hotkeyRecording;
    private bool _overlaysHidden;

    /// <summary>Every binding to register: the saved actions, and each layout's own shortcut.</summary>
    public IReadOnlyList<HotkeyBinding> Hotkeys => [.. _hotkeys, .. LayoutHotkeyBindings()];

    /// <summary>Bindings changed and need registering again.</summary>
    public event Action? HotkeysChanged;

    /// <summary>A shortcut field started (true) or stopped (false) recording.</summary>
    public event Action<bool>? HotkeyRecordingChanged;

    public event Action? ControlPanelToggleRequested;

    /// <summary>Every floating widget hidden at once. Session-only: a restart always brings the
    /// overlays back, so nobody is left wondering where they went.</summary>
    public bool OverlaysHidden
    {
        get => _overlaysHidden;
        set
        {
            if (_overlaysHidden == value)
            {
                return;
            }

            _overlaysHidden = value;
            AppLog.Activity("Control Panel", value ? "All overlays hidden" : "All overlays shown");
            foreach (var slot in _slots.Values)
            {
                slot.IsSuppressed = value;
            }

            OnPropertyChanged();
            RefreshHome();
        }
    }

    public void Execute(string action)
    {
        AppLog.Activity("Hotkeys", $"Pressed: {DescribeAction(action)}");
        if (HotkeyActions.TryGetLayout(action, out var layoutId))
        {
            ToggleLayoutById(layoutId);
            return;
        }

        if (HotkeyActions.TryGetWidget(action, out var widgetKey))
        {
            // Same switch as the widget's own toggle in the rail, so the two can never disagree.
            if (_slots.TryGetValue(widgetKey, out var slot))
            {
                slot.IsEnabled = !slot.IsEnabled;
            }

            return;
        }

        switch (action)
        {
            case HotkeyActions.ToggleOverlays:
                OverlaysHidden = !OverlaysHidden;
                break;
            case HotkeyActions.ToggleEditMode:
                IsEditMode = !IsEditMode;
                break;
            case HotkeyActions.ToggleControlPanel:
                ControlPanelToggleRequested?.Invoke();
                break;
            case HotkeyActions.ResetLayout:
                ResetLayout();
                break;
            case HotkeyActions.RestartOverlays:
                RestartOverlays();
                break;
            case HotkeyActions.ToggleLayout:
                ToggleLayoutCommand.Execute(null);
                break;
            case HotkeyActions.NextLayout:
                CycleLayout(+1);
                break;
            case HotkeyActions.PreviousLayout:
                CycleLayout(-1);
                break;
            case HotkeyActions.EditLayout:
                EditCurrentLayout();
                break;
        }
    }

    /// <summary>Closes and recreates every open widget window — a fresh window clears any rendering
    /// glitch without restarting the app. Positions, sizes and options all carry over.</summary>
    public void RestartOverlays()
    {
        AppLog.Activity("Control Panel", "Overlay windows restarted");
        foreach (var slot in _slots.Values)
        {
            slot.Restart();
        }
    }

    /// <summary>Every widget back to its default position, including ones not opened yet.</summary>
    public void ResetLayout()
    {
        AppLog.Activity("Control Panel", "Overlay positions reset");
        foreach (var slot in _slots.Values)
        {
            slot.ResetPosition();
        }
    }

    public void ReportHotkeyFailures(IReadOnlySet<string> failed)
    {
        _hotkeyFailures = failed;
        if (failed.Count > 0)
        {
            AppLog.Warn("Hotkeys", $"Windows refused {failed.Count} shortcut(s): {string.Join(", ", failed.Select(DescribeAction))}");
        }

        foreach (var (action, setting) in _hotkeySettings)
        {
            setting.SetStatus(failed.Contains(action) ? HotkeyTakenMessage : null);
        }
    }

    public static string Describe(string action)
    {
        if (HotkeyActions.TryGetWidget(action, out var widgetKey))
        {
            var name = WidgetCatalog.All.FirstOrDefault(d => d.Key == widgetKey)?.Name ?? widgetKey;
            return Loc.F("hotkeys.toggleWidget", name);
        }

        return action switch
        {
            HotkeyActions.ToggleOverlays => Loc.T("hotkeys.showHideAllOverlays"),
            HotkeyActions.ToggleEditMode => Loc.T("hotkeys.moveWidgetsOnOff"),
            HotkeyActions.ToggleControlPanel => Loc.T("hotkeys.showHideControlPanel"),
            HotkeyActions.RestartOverlays => Loc.T("hotkeys.restartOverlays"),
            HotkeyActions.ToggleLayout => Loc.T("hotkeys.openCloseLayout"),
            HotkeyActions.NextLayout => Loc.T("hotkeys.nextLayout"),
            HotkeyActions.PreviousLayout => Loc.T("hotkeys.previousLayout"),
            HotkeyActions.EditLayout => Loc.T("hotkeys.editLayout"),
            _ => Loc.T("hotkeys.resetOverlayPositions"),
        };
    }

    /// <summary><see cref="Describe"/>, plus a layout's own shortcut, which needs the layout's name.</summary>
    private string DescribeAction(string action) =>
        HotkeyActions.TryGetLayout(action, out var id)
            ? Loc.F("hotkeys.toggleNamedLayout", LayoutStore.Get(id)?.Name ?? Loc.T("hotkeys.deletedLayout"))
            : Describe(action);

    private static string? HintFor(string action) => action switch
    {
        HotkeyActions.ToggleOverlays => Loc.T("hotkeys.floatingWidgetsOnly"),
        HotkeyActions.ToggleControlPanel => null,
        HotkeyActions.RestartOverlays => Loc.T("hotkeys.clearsRenderingGlitches"),
        HotkeyActions.ResetLayout => Loc.T("hotkeys.offByDefault"),
        HotkeyActions.ToggleLayout => Loc.T("hotkeys.theLayoutChosenIn"),
        HotkeyActions.NextLayout => Loc.T("hotkeys.switchesStraightToIt"),
        HotkeyActions.PreviousLayout => Loc.T("hotkeys.switchesStraightToIt"),
        HotkeyActions.EditLayout => Loc.T("hotkeys.theOpenLayoutOr"),
        _ => null,
    };

    private static IEnumerable<string> AllHotkeyActions() =>
        HotkeyActions.Global
            .Concat(HotkeyActions.Layouts)
            .Concat(WidgetCatalog.All.Select(d => HotkeyActions.ToggleWidget(d.Key)));

    private IEnumerable<SettingsGroup> HotkeyGroups()
    {
        // A rebuild while a field was recording removes that field; make sure hotkeys come back.
        if (!_buildingIndex)
        {
            SetHotkeyRecording(false);
            _hotkeySettings.Clear();
        }

        var global = new SettingsGroup(
            Loc.T("hotkeys.overlays"),
            Loc.T("hotkeys.workInAnyApp"));
        var layouts = new SettingsGroup(Loc.T("hotkeys.layouts"), Loc.T("hotkeys.eachLayoutSOwn"));
        var widgets = new SettingsGroup(Loc.T("hotkeys.widgets"), Loc.T("hotkeys.showOrHideOne"));
        foreach (var binding in _hotkeys)
        {
            var action = binding.Action;
            var setting = new HotkeySetting(
                Describe(action),
                HintFor(action),
                binding.Hotkey,
                binding.Enabled,
                hotkey => AssignHotkey(action, hotkey),
                enabled => UpdateHotkey(action, b => b with { Enabled = enabled }),
                SetHotkeyRecording);
            setting.SetStatus(_hotkeyFailures.Contains(action) ? HotkeyTakenMessage : null);
            if (!_buildingIndex)
            {
                _hotkeySettings[action] = setting;
            }
            (HotkeyActions.TryGetWidget(action, out _) ? widgets
                : HotkeyActions.Layouts.Contains(action) ? layouts
                : global).Items.Add(setting);
        }

        return
        [
            global,
            layouts,
            widgets,
            new SettingsGroup(Loc.T("hotkeys.defaults"))
                .With(
                    new ActionSetting(
                        Loc.T("hotkeys.restoreDefaultShortcuts"),
                        DefaultsHint(),
                        Loc.T("hotkeys.restoreDefaults"),
                        RestoreDefaultHotkeysWithUndo) { IsDestructive = true }),
        ];
    }

    /// <summary>The shortcuts that differ from how they came, so "restore" says what it would undo.</summary>
    private List<HotkeyBinding> ChangedFromDefaults() =>
        _hotkeys.Where(binding => HotkeyDefaults.For(binding.Action) is var (hotkey, enabled) &&
                                  (binding.Hotkey != hotkey || binding.Enabled != enabled))
            .ToList();

    private string DefaultsHint() => ChangedFromDefaults().Count switch
    {
        0 => Loc.T("hotkeys.differs.none"),
        var count => Loc.Plural("hotkeys.differs", count),
    };

    /// <summary>Every shortcut back as it came; undo puts back the ones you had.</summary>
    private void RestoreDefaultHotkeysWithUndo()
    {
        var changed = ChangedFromDefaults().Count;
        if (changed == 0)
        {
            ShowToast(Loc.T("hotkeys.yourShortcutsAreAlready"), null);
            return;
        }

        var before = _hotkeys.ToList();
        RestoreDefaultHotkeys();
        ShowToast(Loc.Plural("hotkeys.restored", changed), () =>
        {
            _hotkeys = before;
            HotkeyStore.Save(_hotkeys);
            HotkeysChanged?.Invoke();
            RebuildPage();
        });
    }


    /// <summary>Refuses a combination another action already has; null means it was taken.</summary>
    private string? AssignHotkey(string action, Hotkey? hotkey)
    {
        if (TakenBy(action, hotkey) is { } taken)
        {
            return taken;
        }

        UpdateHotkey(action, b => b with { Hotkey = hotkey });
        return null;
    }

    /// <summary>Why <paramref name="hotkey"/> can't go to <paramref name="action"/> — another action
    /// or layout has it — or null if it can.</summary>
    private string? TakenBy(string action, Hotkey? hotkey) =>
        hotkey is not null && Hotkeys.FirstOrDefault(b => b.Action != action && b.Hotkey == hotkey) is { } other
            ? Loc.F("hotkeys.takenBy", DescribeAction(other.Action))
            : null;

    private void UpdateHotkey(string action, Func<HotkeyBinding, HotkeyBinding> change)
    {
        var index = _hotkeys.FindIndex(b => b.Action == action);
        _hotkeys[index] = change(_hotkeys[index]);
        HotkeyStore.Save(_hotkeys);
        HotkeysChanged?.Invoke();
    }

    private void RestoreDefaultHotkeys()
    {
        _hotkeys = AllHotkeyActions()
            .Select(action =>
            {
                var (hotkey, enabled) = HotkeyDefaults.For(action);
                return new HotkeyBinding(action, hotkey, enabled);
            })
            .ToList();
        HotkeyStore.Save(_hotkeys);
        HotkeysChanged?.Invoke();
        if (Selected is { } page)
        {
            BuildSettings(page);
        }
    }

    private void SetHotkeyRecording(bool recording)
    {
        if (_hotkeyRecording == recording)
        {
            return;
        }

        _hotkeyRecording = recording;
        HotkeyRecordingChanged?.Invoke(recording);
    }
}
