using System.Collections.ObjectModel;
using System.ComponentModel;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>A heading in the rail (RACE, SETTINGS…) that can be folded to hide its pages. One
/// instance per title, so the rail can group on it; folded or not is remembered. A title starting
/// with "#" (or empty) is a section without a heading — a page that stands on its own, like Home or
/// Layouts — and the rest of the title only tells such sections apart. The title is also what the
/// folded state is saved under, so it stays the same in every language; <see cref="Heading"/> is
/// what the rail shows.</summary>
public sealed class NavSection : INotifyPropertyChanged
{
    private static readonly Dictionary<string, NavSection> ByTitle = [];

    private string _badge = "";
    private string? _badgeTip;
    private bool _hasActive;

    private NavSection(string title)
    {
        Title = title;
        Loc.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Heading)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public bool HasHeading => Title.Length > 0 && Title[0] != '#';

    /// <summary>The heading as shown, in the current language.</summary>
    public string Heading => HasHeading ? Loc.T($"nav.section.{Title}") : "";

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

    /// <summary>Whether any of the section's widgets is switched on, which lights the count.</summary>
    public bool HasActive
    {
        get => _hasActive;
        set
        {
            if (_hasActive == value)
            {
                return;
            }

            _hasActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActive)));
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
public sealed class NavItem : INotifyPropertyChanged
{
    private readonly string _titleKey;
    private readonly string _blurbKey;

    private NavItem(string key, string titleKey, string blurbKey, string iconData, string group, WidgetSlot? widget, bool isFooter = false)
    {
        IsFooter = isFooter;
        Key = key;
        _titleKey = titleKey;
        _blurbKey = blurbKey;
        IconData = iconData;
        Group = group;
        Widget = widget;
        Loc.Changed += () =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Blurb)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tabs)));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }
    public string Title => Loc.T(_titleKey);
    public string Blurb => Loc.T(_blurbKey);
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

    /// <summary>Listed at the foot of the rail, above Help, so it stays in reach however long the
    /// widget list grows (Layouts and the settings pages).</summary>
    public bool IsPinned { get; init; }

    /// <summary>Tabs across the top of the page, or empty for a page without any.</summary>
    public IReadOnlyList<string> Tabs => TabKeys.Select(Loc.T).ToList();

    /// <summary>The tabs' text keys, in order.</summary>
    public IReadOnlyList<string> TabKeys { get; init; } = [];

    public bool HasTabs => TabKeys.Count > 0;

    public ObservableCollection<SettingsGroup> Settings { get; } = [];

    /// <summary>A widget's page, under the heading of its category.</summary>
    public static NavItem ForWidget(WidgetSlot slot) => new(
        slot.Key, slot.Descriptor.NameKey, slot.Descriptor.BlurbKey, slot.Descriptor.IconData,
        WidgetCatalog.HeadingOf(slot.Descriptor.Category), slot);

    /// <summary>An application page, its title and blurb given as text keys; an empty
    /// <paramref name="group"/> lists it without a heading.</summary>
    public static NavItem ForPage(string key, string titleKey, string blurbKey, string iconData, string group, bool isPinned = false) =>
        new(key, titleKey, blurbKey, iconData, group, widget: null) { IsPinned = isPinned };

    /// <summary>A page reached from the foot of the rail rather than listed in it.</summary>
    public static NavItem ForFooter(string key, string titleKey, string blurbKey, string iconData, IReadOnlyList<string> tabKeys) =>
        new(key, titleKey, blurbKey, iconData, "", widget: null, isFooter: true) { TabKeys = tabKeys };
}
