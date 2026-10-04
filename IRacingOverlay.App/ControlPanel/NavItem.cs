using System.Collections.ObjectModel;
using System.ComponentModel;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>A heading in the rail (WIDGETS, APPLICATION, ABOUT) that can be folded to hide its
/// pages. One instance per title, so the rail can group on it; folded or not is remembered.</summary>
public sealed class NavSection : INotifyPropertyChanged
{
    private static readonly Dictionary<string, NavSection> ByTitle = [];

    private NavSection(string title) => Title = title;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public bool IsCollapsed
    {
        get => CollapseStore.IsCollapsed(Key);
        set
        {
            if (value == IsCollapsed)
            {
                return;
            }

            CollapseStore.SetCollapsed(Key, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCollapsed)));
        }
    }

    private string Key => $"Rail/{Title}";

    public static NavSection For(string title)
    {
        if (!ByTitle.TryGetValue(title, out var section))
        {
            section = new NavSection(title);
            ByTitle[title] = section;
        }

        return section;
    }

    public override string ToString() => Title;
}

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

    /// <summary>The rail heading as an object, what the rail actually groups on so the heading can fold.</summary>
    public NavSection Section => NavSection.For(Group);

    /// <summary>The widget this page configures, or null for an application-level page.</summary>
    public WidgetSlot? Widget { get; }

    public bool IsWidget => Widget is not null;

    public ObservableCollection<SettingsGroup> Settings { get; } = [];

    public static NavItem ForWidget(WidgetSlot slot) => new(
        slot.Key, slot.Descriptor.Name, slot.Descriptor.Blurb, slot.Descriptor.IconData, "WIDGETS", slot);

    public static NavItem ForPage(string key, string title, string blurb, string iconData, string group = "APPLICATION") =>
        new(key, title, blurb, iconData, group, widget: null);
}
