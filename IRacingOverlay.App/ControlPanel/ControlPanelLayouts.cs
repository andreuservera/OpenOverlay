using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.Overlay;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// The Layouts page: pick a layout, edit it, rename, duplicate or delete it, or create a new one.
/// The page is a list of ordinary settings rows like every other page; the designing itself happens
/// in <see cref="LayoutEditorWindow"/>, which needs more room than this pane has.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private const string LayoutsPageKey = "app.layouts";

    private LayoutStore? _layoutStore;
    private MonitorCatalog? _monitorCatalog;
    private Guid? _selectedLayoutId;
    private LayoutEditorWindow? _layoutEditor;
    private LayoutSession? _layoutSession;
    private bool _stackingPending;
    private Layout? _previewLayout;
    private IReadOnlyList<LayoutChoice>? _layoutChoices;
    private RelayCommand? _toggleLayout;
    private RelayCommand? _goToLayouts;
    private string _layoutHotkeySignature = "";

    /// <summary>A short message to show outside the control panel (title, text): a layout switched
    /// to by shortcut, while the sim has the screen.</summary>
    public event Action<string, string>? LayoutSwitchedNotice;

    // Created on first use: nothing about layouts should cost anything until the page is opened.
    internal LayoutStore LayoutStore => _layoutStore ??= new LayoutStore();

    internal MonitorCatalog Monitors => _monitorCatalog ??= new MonitorCatalog(new SystemMonitorSource());

    private LayoutSession LayoutSession => _layoutSession ??= new LayoutSession(LayoutStore, new SlotLayoutHost(SlotOf, CreateConfigCodecs));

    /// <summary>The open layout's name, for the toolbar; null when none is open. A layout left open
    /// when the app last exited is still open: its widgets kept the layout's settings.</summary>
    public string? OpenLayoutName => LayoutStore.Open is { } open
        ? LayoutStore.Get(open.LayoutId)?.Name ?? "Deleted layout"
        : null;

    public bool HasOpenLayout => LayoutStore.Open is not null;

    // ===== Toolbar =====
    //
    // A layout picker and a power button, always there: pick a layout and switch it on; while one
    // is open, picking another switches to it. Independent of the Layouts page's own dropdown, which
    // picks the layout to look at and edit — browsing there must never change what is on screen.

    /// <summary>Every layout, by name, for the toolbar's dropdown.</summary>
    public IReadOnlyList<LayoutChoice> LayoutChoices =>
        _layoutChoices ??= LayoutStore.List().Select(layout => new LayoutChoice(layout.Id, layout.Name)).ToList();

    public bool HasLayouts => LayoutChoices.Count > 0;

    /// <summary>The open layout; with none open, the one last picked (remembered across restarts),
    /// or the first. Picking one while a layout is open switches to it; otherwise it only becomes
    /// the one the power button opens.</summary>
    public LayoutChoice? ToolbarLayout
    {
        get
        {
            if (LayoutStore.Open is { } open)
            {
                return LayoutChoices.FirstOrDefault(choice => choice.Id == open.LayoutId);
            }

            return LayoutChoices.FirstOrDefault(choice => choice.Id == LayoutStore.LastChosen) ?? LayoutChoices.FirstOrDefault();
        }
        set
        {
            if (value is null || value == ToolbarLayout)
            {
                return;
            }

            // After the dropdown has finished closing: opening may ask questions in dialogs, and a
            // cancelled switch has to put the dropdown back on the layout that stayed open.
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (LayoutStore.Open is not null)
                {
                    OpenLayout(value.Id);
                }
                else
                {
                    LayoutStore.SetLastChosen(value.Id);
                }

                OnPropertyChanged(nameof(ToolbarLayout));
            });
        }
    }

    public string LayoutToggleTip => HasOpenLayout
        ? "Close the layout: its widgets go back to how they were before it opened."
        : "Open this layout: its widgets take their place and settings from it.";

    public ICommand ToggleLayoutCommand => _toggleLayout ??= new RelayCommand(() =>
    {
        if (HasOpenLayout)
        {
            CloseLayout();
        }
        else if (ToolbarLayout is { } choice)
        {
            OpenLayout(choice.Id);
        }
    });

    public ICommand GoToLayoutsCommand => _goToLayouts ??= new RelayCommand(() =>
        Selected = NavItems.FirstOrDefault(item => item.Key == LayoutsPageKey));

    private void RefreshLayoutChoices()
    {
        _layoutChoices = null;
        OnPropertyChanged(nameof(LayoutChoices));
        OnPropertyChanged(nameof(HasLayouts));
        OnPropertyChanged(nameof(ToolbarLayout));
    }

    /// <summary>The layout picked on the Layouts page, for the preview pane; null on any other page,
    /// so its panels are let go when the page is left.</summary>
    public Layout? PreviewLayout => Selected?.Key == LayoutsPageKey ? _previewLayout : null;

    public bool HasLayoutPreview => PreviewLayout is not null;

    /// <summary>Points the preview at the page's selected layout. A rebuild of the page that finds
    /// the same layout unchanged keeps the preview as is, rather than rebuilding every panel.</summary>
    private void SetPreviewLayout(Layout? layout)
    {
        if (layout?.Id == _previewLayout?.Id && layout?.ModifiedUtc == _previewLayout?.ModifiedUtc)
        {
            return;
        }

        _previewLayout = layout;
        OnPropertyChanged(nameof(PreviewLayout));
        OnPropertyChanged(nameof(HasLayoutPreview));
        OnPropertyChanged(nameof(ShowsPreviewPlaceholder));
    }

    /// <summary>The page notice for a widget the open layout controls, or null.</summary>
    private SettingsGroup? LayoutNotice(string type)
    {
        if (LayoutStore.Open is not { } open || open.Snapshot.All(entry => entry.Type != type))
        {
            return null;
        }

        return new SettingsGroup(
            $"CONTROLLED BY LAYOUT \"{OpenLayoutName}\"",
            "Changes here apply now but aren't saved in the layout, and are undone when it closes. Edit the layout to change it for good.")
        {
            IsWarning = true,
        };
    }

    /// <summary>Closes the layout editor, asking about unsaved changes. False when the user chose
    /// to keep it open, in which case whatever was closing the application should not go ahead.</summary>
    public bool CloseLayoutEditor()
    {
        _layoutEditor?.Close();
        return _layoutEditor is null;
    }

    private IEnumerable<SettingsGroup> LayoutsPage()
    {
        var layouts = LayoutStore.List();
        var create = new ActionSetting(
            "New layout",
            "For one monitor, at its resolution or one you choose.",
            "Create",
            CreateLayout);
        var import = new ActionSetting(
            "Import layout",
            "From a .layout.json file exported from this app.",
            "Import",
            ImportLayout);

        if (layouts.Count == 0)
        {
            if (!_buildingIndex)
            {
                SetPreviewLayout(null);
            }

            return
            [
                new SettingsGroup(
                    "LAYOUTS",
                    "A layout is a saved arrangement of your widgets — where each goes, how big and how it's set up — for one monitor.")
                    .With(create, import),
            ];
        }

        var selected = layouts.FirstOrDefault(layout => layout.Id == _selectedLayoutId) ?? layouts[0];
        if (!_buildingIndex)
        {
            _selectedLayoutId = selected.Id;
            SetPreviewLayout(selected);
        }

        var openId = LayoutStore.Open?.LayoutId;
        SettingItem openOrStatus = selected.Id == openId
            ? new InfoSetting("Status", "Close it from OPEN NOW above.", "Open now")
            : new ActionSetting(
                "Open layout",
                openId is null ? "Shows its widgets on its monitor, set up as designed." : $"Switches from \"{OpenLayoutName}\".",
                "Open",
                () => OpenLayout(selected.Id));

        List<SettingsGroup> groups = [];
        if (openId is not null)
        {
            groups.Add(new SettingsGroup("OPEN NOW", "Its widgets follow the layout until you close it.")
                .With(
                    new InfoSetting("Layout", null, OpenLayoutName ?? ""),
                    new ActionSetting("Close layout", "Puts its widgets back as they were before it opened.", "Close", CloseLayout)));
        }

        return
        [
            .. groups,
            new SettingsGroup("LAYOUT")
                .With(
                    new ChoiceSetting(
                        "Layout",
                        null,
                        layouts.Select(layout => layout.Name).ToList(),
                        layouts.ToList().FindIndex(layout => layout.Id == selected.Id),
                        index =>
                        {
                            _selectedLayoutId = layouts[index].Id;
                            RefreshLayoutsPage();
                        }),
                    new InfoSetting("Monitor", null, selected.Monitor.FriendlyName),
                    new InfoSetting("Resolution", null, string.Create(CultureInfo.InvariantCulture, $"{selected.Width}×{selected.Height}")),
                    new InfoSetting("Widgets", null, $"{selected.Widgets.Count} of {Layout.MaxWidgets}"),
                    openOrStatus,
                    new ActionSetting(
                        "Edit layout",
                        null,
                        "Edit",
                        () => OpenLayoutEditor(selected.Id))),
            new SettingsGroup("MANAGE")
                .With(
                    new ActionSetting("Rename", null, "Rename", () => RenameLayout(selected.Id)),
                    new ActionSetting("Duplicate", null, "Duplicate", () => DuplicateLayout(selected.Id)),
                    new ActionSetting("Export", null, "Export", () => ExportLayout(selected.Id)),
                    LayoutShortcut(selected),
                    new ActionSetting("Delete", "Can't be undone.", "Delete", () => DeleteLayout(selected.Id))),
            new SettingsGroup("NEW")
                .With(create, import),
        ];
    }

    private static Window? DialogOwner => Application.Current?.MainWindow;

    private void CreateLayout()
    {
        if (LayoutTargetDialog.ForNew(DialogOwner, Monitors.Monitors(), LayoutStore.List().Select(layout => layout.Name).ToList()) is not { } target)
        {
            return;
        }

        var layout = LayoutStore.Create(target.Name, target.Monitor, target.Width, target.Height);
        AppLog.Activity("Layouts", $"Created layout \"{layout.Name}\"");
        _selectedLayoutId = layout.Id;
        RefreshLayoutsPage();
        OpenLayoutEditor(layout.Id);
    }

    private void RenameLayout(Guid id)
    {
        if (LayoutStore.Get(id) is not { } layout ||
            LayoutDialog.Prompt(DialogOwner, "Rename layout", "", layout.Name, "Rename") is not { } name)
        {
            return;
        }

        var used = LayoutStore.Rename(id, name);
        if (used is not null && _layoutEditor?.LayoutId == id)
        {
            _layoutEditor.AdoptName(used);
        }

        if (LayoutStore.Open?.LayoutId == id)
        {
            // The toolbar and the "controlled by" notices name it.
            OnLayoutOpenChanged();
        }

        RefreshLayoutsPage();
    }

    private void DuplicateLayout(Guid id)
    {
        if (LayoutStore.Duplicate(id) is { } copy)
        {
            AppLog.Activity("Layouts", $"Duplicated layout as \"{copy.Name}\"");
            _selectedLayoutId = copy.Id;
            RefreshLayoutsPage();
        }
    }

    private void DeleteLayout(Guid id)
    {
        if (LayoutStore.Get(id) is not { } layout)
        {
            return;
        }

        var isOpen = LayoutStore.Open?.LayoutId == id;
        var answer = LayoutDialog.Ask(
            DialogOwner,
            $"Delete \"{layout.Name}\"?",
            isOpen
                ? "It's open: it closes first, putting its widgets back as they were. Then it's removed for good."
                : "The layout is removed for good. Your widgets keep their own settings.",
            ["Delete", "Cancel"],
            primary: 1);
        if (answer != 0)
        {
            return;
        }

        if (_layoutEditor?.LayoutId == id)
        {
            _layoutEditor.CloseDiscarding();
        }

        if (isOpen)
        {
            CloseLayout();
        }

        LayoutStore.Delete(id);
        AppLog.Activity("Layouts", $"Deleted layout \"{layout.Name}\"");
        _selectedLayoutId = null;
        RefreshLayoutsPage();
    }

    // ===== Export and import =====

    private void ExportLayout(Guid id)
    {
        if (LayoutStore.Get(id) is not { } layout)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export layout",
            FileName = LayoutFile.FileNameFor(layout.Name),
            Filter = LayoutFile.DialogFilter,
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (!ShowFileDialog(dialog))
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, LayoutFile.Export(layout, CreateConfigCodecs(), BuildInfo.Version, DateTime.UtcNow));
            AppLog.Activity("Layouts", $"Exported layout \"{layout.Name}\"");
        }
        catch (Exception error) when (!ExceptionPolicy.IsFatal(error))
        {
            AppLog.Error("Layouts", "Could not export the layout", error);
            LayoutDialog.Inform(DialogOwner, "Could not export the layout", error.Message);
        }
    }

    /// <summary>
    /// Reads a layout file, asks which monitor to aim it at (the file's own if this computer has
    /// it, otherwise the primary) and, if that monitor's resolution differs, whether to scale the
    /// positions. Saved as a new layout, renamed if its name is taken. A file that can't be imported
    /// says why and creates nothing.
    /// </summary>
    private void ImportLayout()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import layout",
            Filter = LayoutFile.DialogFilter,
            CheckFileExists = true,
        };
        if (!ShowFileDialog(dialog))
        {
            return;
        }

        LayoutImport read;
        try
        {
            read = new FileInfo(dialog.FileName).Length > LayoutFile.MaxFileBytes
                ? LayoutImport.Failed("The file is far too big to be a layout file.")
                : LayoutFile.Import(File.ReadAllText(dialog.FileName), CreateConfigCodecs(), LayoutFile.NameFromFileName(dialog.FileName));
        }
        catch (Exception error) when (!ExceptionPolicy.IsFatal(error))
        {
            AppLog.Error("Layouts", "Could not read the layout file", error);
            LayoutDialog.Inform(DialogOwner, "Could not read the file", error.Message);
            return;
        }

        if (read.Layout is not { } layout)
        {
            AppLog.Warn("Layouts", $"Import refused: {read.Error}");
            LayoutDialog.Inform(DialogOwner, "Can't import this layout", read.Error ?? "");
            return;
        }

        if (LayoutTargetDialog.ForImport(DialogOwner, Monitors.Monitors(), LayoutStore.List().Select(saved => saved.Name).ToList(), layout) is not { } target)
        {
            return;
        }

        var scale = false;
        if (target.Width != layout.Width || target.Height != layout.Height)
        {
            var answer = LayoutDialog.Ask(
                DialogOwner,
                "Different resolution",
                string.Create(CultureInfo.InvariantCulture,
                    $"The layout is designed for {layout.Width}×{layout.Height}, and you chose {target.Width}×{target.Height}. Scale the widgets' positions to keep their place on screen, or keep their pixel positions? Sizes don't change."),
                ["Scale positions", "Keep pixels", "Cancel"]);
            if (answer is < 0 or 2)
            {
                return;
            }

            scale = answer == 0;
        }

        layout.Name = target.Name;
        layout.Retarget(target.Monitor, target.Width, target.Height, scale);
        var saved = LayoutStore.Save(layout);
        AppLog.Activity("Layouts", $"Imported layout \"{saved.Name}\"");
        foreach (var warning in read.Warnings)
        {
            AppLog.Warn("Layouts", $"Import of \"{saved.Name}\": {warning}");
        }

        _selectedLayoutId = saved.Id;
        RefreshLayoutsPage();
        if (read.Warnings.Count > 0)
        {
            LayoutDialog.Inform(DialogOwner, $"Imported \"{saved.Name}\"", string.Join(Environment.NewLine + Environment.NewLine, read.Warnings));
        }
    }

    private static bool ShowFileDialog(Microsoft.Win32.FileDialog dialog) =>
        (DialogOwner is { IsVisible: true } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    private void OpenLayoutEditor(Guid id)
    {
        if (LayoutStore.Get(id) is not { } layout)
        {
            return;
        }

        if (_layoutEditor is { } editor)
        {
            if (editor.TryOpen(layout))
            {
                editor.Activate();
            }

            return;
        }

        editor = new LayoutEditorWindow(LayoutStore, Monitors, NewLayoutWidget, layout);
        editor.Saved += () =>
        {
            ReapplyIfOpen(editor.LayoutId);
            RefreshLayoutsPage();
        };
        editor.Closed += (_, _) =>
        {
            if (ReferenceEquals(_layoutEditor, editor))
            {
                _layoutEditor = null;
            }
        };
        _layoutEditor = editor;
        editor.Show();
    }

    // ===== Open and close =====

    /// <summary>
    /// Opens a layout on its monitor. If that monitor isn't connected it opens on the primary one,
    /// and the user is told; if the monitor's resolution differs from the layout's, the user chooses
    /// whether to scale the positions. Neither choice changes the saved layout.
    /// </summary>
    private void OpenLayout(Guid id)
    {
        if (LayoutStore.Get(id) is not { } layout)
        {
            return;
        }

        if (Monitors.Resolve(layout.Monitor) is not { } found)
        {
            LayoutDialog.Inform(DialogOwner, "No monitor found", "Windows isn't reporting any monitor to open the layout on.");
            return;
        }

        if (found.IsFallback)
        {
            LayoutDialog.Inform(
                DialogOwner,
                "Monitor not connected",
                $"\"{layout.Monitor.FriendlyName}\" isn't connected, so \"{layout.Name}\" opens on the primary monitor, {found.Monitor.FriendlyName}.");
        }

        var scale = false;
        var monitor = found.Monitor;
        if (monitor.Width != layout.Width || monitor.Height != layout.Height)
        {
            var answer = LayoutDialog.Ask(
                DialogOwner,
                "Different resolution",
                string.Create(CultureInfo.InvariantCulture,
                    $"\"{layout.Name}\" is designed for {layout.Width}×{layout.Height}, and {monitor.FriendlyName} is {monitor.Width}×{monitor.Height}. Scale the widgets' positions to fit, or keep their pixel positions? Sizes don't change."),
                ["Scale positions", "Keep pixels", "Cancel"]);
            if (answer is < 0 or 2)
            {
                return;
            }

            scale = answer == 0;
        }

        LayoutSession.Open(layout, new LayoutPlacement(monitor, scale, PixelsPerDip()));
        LayoutStore.SetLastChosen(layout.Id);
        RequestStacking();
        OnLayoutOpenChanged();
    }

    private void CloseLayout()
    {
        if (LayoutStore.Open is null)
        {
            return;
        }

        LayoutSession.Close();
        OnLayoutOpenChanged();
    }

    /// <summary>The open layout was edited and saved: show the change, placed as it was opened.</summary>
    private void ReapplyIfOpen(Guid id)
    {
        if (LayoutStore.Open is not { } open || open.LayoutId != id ||
            LayoutStore.Get(id) is not { } layout ||
            Monitors.Resolve(layout.Monitor) is not { } found)
        {
            return;
        }

        LayoutSession.Reapply(layout, new LayoutPlacement(found.Monitor, open.ScalePositions, PixelsPerDip()));
        RequestStacking();
        OnLayoutOpenChanged();
    }

    // ===== Stacking order =====
    //
    // Every widget is its own topmost window, and among topmost windows Windows puts on top the one
    // shown last. Applying a layout bottom layer first gets that right only for widgets that were
    // off; one already on keeps its place, and one created later (auto-hide defers creating it until
    // you drive) lands on top. So the open layout's widgets are re-stacked explicitly after it is
    // applied and whenever one of them comes on screen. Only the order changes: nothing is moved or
    // activated, and it runs on those moments only, never per frame.

    private void OnWidgetCameOnScreen(WidgetSlot slot)
    {
        if (LayoutStore.Open is { } open && open.Snapshot.Any(entry => entry.Type == slot.Key))
        {
            RequestStacking();
        }
    }

    /// <summary>Stacks once after the current work, however many widgets appear in one go.</summary>
    private void RequestStacking()
    {
        if (_stackingPending || Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        _stackingPending = true;
        dispatcher.BeginInvoke(() =>
        {
            _stackingPending = false;
            StackOpenLayout();
        });
    }

    /// <summary>Puts the open layout's widgets in the editor's order, the front one on top.</summary>
    private void StackOpenLayout()
    {
        if (LayoutStore.Open is not { } open || LayoutStore.Get(open.LayoutId) is not { } layout)
        {
            return;
        }

        NativeMethods.StackTopmost(layout.ControlledBottomToTop()
            .Select(widget => SlotOf(widget.Type).Window)
            .OfType<OverlayWindowBase>()
            .Select(window => new WindowInteropHelper(window).Handle));
    }

    private void OnLayoutOpenChanged()
    {
        OnPropertyChanged(nameof(OpenLayoutName));
        OnPropertyChanged(nameof(HasOpenLayout));
        OnPropertyChanged(nameof(ToolbarLayout));
        OnPropertyChanged(nameof(LayoutToggleTip));
        // Both the Layouts page and a widget page may now read differently.
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Selected is { } page && (page.Key == LayoutsPageKey || page.IsWidget))
            {
                BuildSettings(page);
            }
        });
    }

    /// <summary>Physical pixels per WPF unit. The app is system-DPI aware, so one factor holds for
    /// the whole desktop: the primary monitor's pixel width over its width in WPF units.</summary>
    private static double PixelsPerDip() =>
        Screen.PrimaryScreen is { } primary && SystemParameters.PrimaryScreenWidth > 0
            ? primary.Bounds.Width / SystemParameters.PrimaryScreenWidth
            : 1;

    /// <summary>A widget as it is set up individually right now — size, opacity, auto-hide and its
    /// own options — which is where a widget newly added to a layout starts from.</summary>
    private LayoutWidget NewLayoutWidget(string type)
    {
        var slot = SlotOf(type);
        return new LayoutWidget
        {
            Type = type,
            Scale = slot.Scale,
            Opacity = slot.Opacity,
            HideOutsideCar = slot.HideOutsideCar,
            Config = CreateConfigCodecs()[type].Read(),
        };
    }

    /// <summary>Rebuilt after the click that caused it has finished, so the dropdown that raised a
    /// change is not torn down inside its own event.</summary>
    private void RefreshLayoutsPage()
    {
        // Every change to the saved layouts comes through here, so the toolbar's list follows too,
        // and so do the layouts' shortcuts (one deleted takes its shortcut with it).
        RefreshLayoutChoices();
        SyncLayoutHotkeys();
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Selected?.Key == LayoutsPageKey)
            {
                BuildSettings(Selected);
            }
        });
    }
    // ===== Shortcuts =====

    private IEnumerable<HotkeyBinding> LayoutHotkeyBindings() =>
        LayoutStore.List()
            .Where(layout => layout.Hotkey is not null)
            .Select(layout => new HotkeyBinding(HotkeyActions.OpenLayout(layout.Id), layout.Hotkey, layout.HotkeyEnabled));

    /// <summary>Registers the layouts' shortcuts again if any changed. Saves of a layout's content
    /// (the editor auto-saving as you go) leave them alone, so nothing is registered for those.</summary>
    private void SyncLayoutHotkeys()
    {
        var signature = string.Join(";", LayoutHotkeyBindings().Select(b => $"{b.Action}={b.Hotkey?.Display}:{b.Enabled}"));
        if (signature == _layoutHotkeySignature)
        {
            return;
        }

        _layoutHotkeySignature = signature;
        HotkeysChanged?.Invoke();
    }

    /// <summary>The layout's own shortcut, on its Layouts page.</summary>
    private HotkeySetting LayoutShortcut(Layout layout)
    {
        var id = layout.Id;
        var action = HotkeyActions.OpenLayout(id);
        var setting = new HotkeySetting(
            "Shortcut",
            "Opens this layout, or closes it if it's open.",
            layout.Hotkey,
            layout.HotkeyEnabled,
            hotkey =>
            {
                if (TakenBy(action, hotkey) is { } taken)
                {
                    return taken;
                }

                LayoutStore.SetHotkey(id, hotkey, LayoutStore.Get(id)?.HotkeyEnabled ?? true);
                SyncLayoutHotkeys();
                return null;
            },
            enabled =>
            {
                LayoutStore.SetHotkey(id, LayoutStore.Get(id)?.Hotkey, enabled);
                SyncLayoutHotkeys();
            },
            SetHotkeyRecording);
        setting.SetStatus(_hotkeyFailures.Contains(action) ? HotkeyTakenMessage : null);
        if (!_buildingIndex)
        {
            _hotkeySettings[action] = setting;
        }

        return setting;
    }

    /// <summary>A layout's own shortcut: opens it, switching from any other, or closes it if it's open.</summary>
    private void ToggleLayoutById(Guid id)
    {
        if (LayoutStore.Open?.LayoutId == id)
        {
            CloseLayout();
        }
        else
        {
            OpenLayout(id);
        }
    }

    /// <summary>Next or previous layout in the list, switched to directly, wrapping round. From the
    /// open layout; with none open, the one chosen in the toolbar opens.</summary>
    private void CycleLayout(int step)
    {
        var layouts = LayoutStore.List().ToList();
        if (layouts.Count == 0)
        {
            return;
        }

        Guid target;
        if (LayoutStore.Open is { } open && layouts.FindIndex(layout => layout.Id == open.LayoutId) is var index and >= 0)
        {
            target = layouts[(index + step + layouts.Count) % layouts.Count].Id;
        }
        else if (ToolbarLayout is { } chosen)
        {
            target = chosen.Id;
        }
        else
        {
            return;
        }

        OpenLayout(target);
        if (LayoutStore.Open?.LayoutId == target)
        {
            LayoutSwitchedNotice?.Invoke("Layout", $"\"{OpenLayoutName}\" is open.");
        }
    }

    /// <summary>The editor on the open layout, or on the one chosen in the toolbar.</summary>
    private void EditCurrentLayout()
    {
        if ((LayoutStore.Open?.LayoutId ?? ToolbarLayout?.Id) is { } id)
        {
            OpenLayoutEditor(id);
        }
    }
}

/// <summary>A layout as the toolbar's dropdown lists it. Compared by value, so a refreshed list
/// still recognises the entry that was selected.</summary>
public sealed record LayoutChoice(Guid Id, string Name)
{
    /// <summary>What the closed dropdown shows: the theme's dropdown draws the selected item as
    /// text, without the DisplayMemberPath the open list uses.</summary>
    public override string ToString() => Name;
}
