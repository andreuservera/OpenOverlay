using System.Globalization;
using System.IO;
using System.Windows;
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
        }

        var openId = LayoutStore.Open?.LayoutId;
        SettingItem openOrStatus = selected.Id == openId
            ? new InfoSetting("Status", "Close it from OPEN NOW above.", "Open now")
            : new ActionSetting(
                "Open layout",
                openId is null ? "Shows its widgets on its monitor, set up as designed." : $"Closes \"{OpenLayoutName}\" first.",
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
                        "Place, size and arrange its widgets.",
                        "Edit",
                        () => OpenLayoutEditor(selected.Id))),
            new SettingsGroup("MANAGE")
                .With(
                    new ActionSetting("Rename", null, "Rename", () => RenameLayout(selected.Id)),
                    new ActionSetting("Duplicate", "A copy you can change without touching this one.", "Duplicate", () => DuplicateLayout(selected.Id)),
                    new ActionSetting("Export", "Save it to a file, to keep or share.", "Export", () => ExportLayout(selected.Id)),
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
    private void RefreshLayoutsPage() =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Selected?.Key == LayoutsPageKey)
            {
                BuildSettings(Selected);
            }
        });
}
