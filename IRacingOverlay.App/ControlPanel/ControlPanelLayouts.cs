using System.Globalization;
using System.Windows;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Layouts;

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

    // Created on first use: nothing about layouts should cost anything until the page is opened.
    internal LayoutStore LayoutStore => _layoutStore ??= new LayoutStore();

    internal MonitorCatalog Monitors => _monitorCatalog ??= new MonitorCatalog(new SystemMonitorSource());

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

        return
        [
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

        var answer = LayoutDialog.Ask(
            DialogOwner,
            $"Delete \"{layout.Name}\"?",
            "The layout is removed for good. Your widgets keep their own settings.",
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
        editor.Saved += RefreshLayoutsPage;
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
