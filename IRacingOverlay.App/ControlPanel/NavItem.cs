using System.Collections.ObjectModel;
using System.ComponentModel;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>A heading in the rail (RACE, SETTINGS…) that can be folded to hide its pages. One
/// instance per title, so the rail can group on it; folded or not is remembered. A title starting
/// with "#" (or empty) is a section without a heading — a page that stands on its own, like Home or
/// Layouts — and the rest of the title only tells such sections apart.</summary>
public sealed class NavSection : INotifyPropertyChanged
{
    private static readonly Dictionary<string, NavSection> ByTitle = [];

    private string _badge = "";
    private string? _badgeTip;

    private NavSection(string title) => Title = title;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public bool HasHeading => Title.Length > 0 && Title[0] != '#';

    public bool IsCollapsed
    {
        get => HasHeading && CollapseStore.IsCollapsed(Key);
        set
        {
            if (!HasHeading || value == IsCollapsed)
            {
                return;
            }

            CollapseStore.SetCollapsed(Key, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCollapsed)));
        }
    }

    /// <summary>A short count beside the heading ("4/12"), or empty for none.</summary>
    public string Badge
    {
        get => _badge;
        set
        {
            if (_badge == value)
            {
                return;
            }

            _badge = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Badge)));
        }
    }

    /// <summary>The count in words, for the tooltip.</summary>
    public string? BadgeTip
    {
        get => _badgeTip;
        set
        {
            if (_badgeTip == value)
            {
                return;
            }

            _badgeTip = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BadgeTip)));
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
    private NavItem(string key, string title, string blurb, string iconData, string group, WidgetSlot? widget, bool isFooter = false)
    {
        IsFooter = isFooter;
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

    /// <summary>Kept out of the list and reached from the foot of the rail instead (Help).</summary>
    public bool IsFooter { get; }

    /// <summary>Tabs across the top of the page, or empty for a page without any.</summary>
    public IReadOnlyList<string> Tabs { get; init; } = [];

    public bool HasTabs => Tabs.Count > 0;

    public ObservableCollection<SettingsGroup> Settings { get; } = [];

    /// <summary>A widget's page, under the heading of its category.</summary>
    public static NavItem ForWidget(WidgetSlot slot) => new(
        slot.Key, slot.Descriptor.Name, slot.Descriptor.Blurb, slot.Descriptor.IconData,
        WidgetCatalog.HeadingOf(slot.Descriptor.Category), slot);

    /// <summary>An application page; an empty <paramref name="group"/> lists it without a heading.</summary>
    public static NavItem ForPage(string key, string title, string blurb, string iconData, string group) =>
        new(key, title, blurb, iconData, group, widget: null);

    /// <summary>A page reached from the foot of the rail rather than listed in it.</summary>
    public static NavItem ForFooter(string key, string title, string blurb, string iconData, IReadOnlyList<string> tabs) =>
        new(key, title, blurb, iconData, "", widget: null, isFooter: true) { Tabs = tabs };
}
