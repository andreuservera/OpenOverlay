using System.Collections.ObjectModel;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// One entry in the left-hand rail, and the page it opens. Widgets and the two application-level
/// pages share this type so the rail is a single list with a single selection — the alternative,
/// two lists that have to agree about which of them currently owns the selection, is a bug waiting
/// to happen and reads as two competing navigations to the user.
/// </summary>
public sealed class NavItem
{
    private NavItem(string key, string title, string blurb, string iconData, string group, WidgetSlot? widget)
    {
        Key = key;
        Title = title;
        Blurb = blurb;
        IconData = iconData;
        Group = group;
        Widget = widget;
    }

    public string Key { get; }
    public string Title { get; }
    public string Blurb { get; }
    public string IconData { get; }

    /// <summary>Rail heading this entry files itself under. The rail groups on this value, so a new
    /// section is a new string rather than new markup.</summary>
    public string Group { get; }

    /// <summary>The widget this page configures, or null for an application-level page.</summary>
    public WidgetSlot? Widget { get; }

    public bool IsWidget => Widget is not null;

    public ObservableCollection<SettingsGroup> Settings { get; } = [];

    public static NavItem ForWidget(WidgetSlot slot) => new(
        slot.Key, slot.Descriptor.Name, slot.Descriptor.Blurb, slot.Descriptor.IconData, "WIDGETS", slot);

    public static NavItem ForPage(string key, string title, string blurb, string iconData) =>
        new(key, title, blurb, iconData, "APPLICATION", widget: null);
}
