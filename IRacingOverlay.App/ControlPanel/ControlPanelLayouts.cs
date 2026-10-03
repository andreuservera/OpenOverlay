using System.Globalization;
using System.Windows;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Layouts;
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

        if (layouts.Count == 0)
        {
            return
            [
                new SettingsGroup(
                    "LAYOUTS",
                    "A layout is a saved arrangement of your widgets — where each goes, how big and how it's set up — for one monitor.")
                    .With(create),
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
                    new ActionSetting("Delete", "Can't be undone.", "Delete", () => DeleteLayout(selected.Id))),
            new SettingsGroup("NEW")
                .With(create),
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
        OnLayoutOpenChanged();
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
