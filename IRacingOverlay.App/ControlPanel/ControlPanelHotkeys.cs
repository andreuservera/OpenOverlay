using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Global hotkeys: the saved bindings, the actions they trigger, and their groups on the General
/// page. Registration with Windows lives in MainWindow's <see cref="GlobalHotkeyManager"/>; this side
/// only says what the bindings are and reports back which ones Windows refused.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private const string HotkeyTakenMessage = "Windows or another app is already using this shortcut.";

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
            return $"Show / hide {name}";
        }

        return action switch
        {
            HotkeyActions.ToggleOverlays => "Show / hide all overlays",
            HotkeyActions.ToggleEditMode => "Lock / unlock widgets",
            HotkeyActions.ToggleControlPanel => "Show / hide Control Panel",
            HotkeyActions.RestartOverlays => "Restart overlays",
            HotkeyActions.ToggleLayout => "Open / close layout",
            HotkeyActions.NextLayout => "Next layout",
            HotkeyActions.PreviousLayout => "Previous layout",
            HotkeyActions.EditLayout => "Edit layout",
            _ => "Reset overlay positions",
        };
    }

    /// <summary><see cref="Describe"/>, plus a layout's own shortcut, which needs the layout's name.</summary>
    private string DescribeAction(string action) =>
        HotkeyActions.TryGetLayout(action, out var id)
            ? $"Open / close layout \"{LayoutStore.Get(id)?.Name ?? "deleted layout"}\""
            : Describe(action);

    private static string? HintFor(string action) => action switch
    {
        HotkeyActions.ToggleOverlays => "Floating widgets only.",
        HotkeyActions.ToggleControlPanel => null,
        HotkeyActions.RestartOverlays => "Clears rendering glitches.",
        HotkeyActions.ResetLayout => "Off by default.",
        HotkeyActions.ToggleLayout => "The layout chosen in the toolbar.",
        HotkeyActions.NextLayout => "Switches straight to it; a notice names it.",
        HotkeyActions.PreviousLayout => "Switches straight to it; a notice names it.",
        HotkeyActions.EditLayout => "The open layout, or the one chosen in the toolbar.",
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
            "HOTKEYS",
            "Work in any app. Click a shortcut and press new keys; Esc cancels. Avoid keys iRacing uses.");
        var layouts = new SettingsGroup("HOTKEYS · LAYOUTS", "Each layout's own shortcut is set on the Layouts page.");
        var widgets = new SettingsGroup("HOTKEYS · WIDGETS");
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
            new SettingsGroup("HOTKEYS · RESET")
                .With(
                    new ActionSetting(
                        "Default shortcuts",
                        "Ctrl + Shift + F8 to F12, no widget shortcuts.",
                        "Restore defaults",
                        RestoreDefaultHotkeys),
                    new ActionSetting(
                        "Overlay positions",
                        "Move every widget back to its default spot.",
                        "Reset positions",
                        ResetLayout)),
        ];
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
            ? $"Already used by \"{DescribeAction(other.Action)}\"."
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
