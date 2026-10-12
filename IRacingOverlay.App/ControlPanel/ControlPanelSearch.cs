using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Search that lands on the setting, not near it. Typing lists every page and option that matches —
/// its label, its explanation, its choices, the columns or flags inside it — each with where it
/// lives. Picking one opens that page (on the right tab), unfolds the section it is in, scrolls it
/// into view and lights the row for a moment.
///
/// Matching on what a page is for as well as its name is deliberate: it lets someone find the widget
/// by what it does ("fuel", "gap") before they know what this application decided to call it.
/// </summary>
public sealed partial class ControlPanelViewModel
{
    private const int MaxSearchResults = 60;

    private string _searchText = "";
    private List<SearchEntry>? _searchIndex;
    private RelayCommand? _clearSearch;
    private DispatcherTimer? _highlightTimer;
    private SettingItem? _highlighted;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value;
            UpdateSearchResults();
            OnPropertyChanged();
        }
    }

    public bool IsSearching => _searchText.Trim().Length > 0;

    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    /// <summary>The results pane's heading: how many, for what.</summary>
    public string SearchSummary => SearchResults.Count switch
    {
        0 => $"Nothing matches “{_searchText.Trim()}”",
        1 => $"1 result for “{_searchText.Trim()}”",
        var count => $"{count}{(count >= MaxSearchResults ? "+" : "")} results for “{_searchText.Trim()}”",
    };

    public ICommand ClearSearchCommand => _clearSearch ??= new RelayCommand(ClearSearch);

    /// <summary>A setting a search result led to is on screen and should be scrolled into view.</summary>
    public event Action<SettingItem>? RevealRequested;

    public void ClearSearch() => SearchText = "";

    private void UpdateSearchResults()
    {
        SearchResults.Clear();
        var term = _searchText.Trim();
        if (term.Length == 0)
        {
            _searchIndex = null;
        }
        else
        {
            // Built once per search, not per keystroke: every page's options, as plain text.
            _searchIndex ??= BuildSearchIndex();
            foreach (var entry in _searchIndex.Where(entry => entry.Matches(term)).Take(MaxSearchResults))
            {
                SearchResults.Add(new SearchResult(entry.Title, entry.Path, entry.Detail, entry.Page.IconData, new RelayCommand(() => OpenResult(entry))));
            }
        }

        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(SearchSummary));
    }

    /// <summary>Every page, then every option on it, in rail order — so results read top to bottom the
    /// way the rail does.</summary>
    private List<SearchEntry> BuildSearchIndex()
    {
        var pages = new List<SearchEntry>();
        var settings = new List<SearchEntry>();
        var tab = _selectedTab;
        _buildingIndex = true;
        try
        {
            foreach (var page in NavItems)
            {
                var heading = page.IsFooter ? "Help" : page.Section.HasHeading ? Capitalize(page.Section.Title) : "Page";
                pages.Add(new SearchEntry(page, 0, null, null, page.Title, heading, page.Blurb, [page.Title, page.Blurb]));

                var tabs = page.HasTabs ? Enumerable.Range(0, page.Tabs.Count) : [0];
                foreach (var index in tabs)
                {
                    _selectedTab = index;
                    var place = page.HasTabs ? $"{page.Title} › {page.Tabs[index]}" : page.Title;
                    foreach (var group in BuildGroups(page))
                    {
                        settings.AddRange(EntriesOf(page, index, place, group));
                    }
                }
            }
        }
        finally
        {
            _buildingIndex = false;
            _selectedTab = tab;
        }

        return [.. pages, .. settings];
    }

    private static IEnumerable<SearchEntry> EntriesOf(NavItem page, int tab, string place, SettingsGroup group)
    {
        var section = Capitalize(group.Title);
        foreach (var item in group.Items)
        {
            // Release notes are prose about the app, not options in it.
            if (item is BulletSetting or ReleaseSetting)
            {
                continue;
            }

            var path = $"{place} › {section}";
            IEnumerable<string> choices = item switch
            {
                ChoiceSetting choice => choice.Options,
                SegmentedSetting segmented => segmented.Options,
                HotkeySetting hotkey => [hotkey.Display],
                _ => [],
            };
            yield return new SearchEntry(page, tab, group.Title, item.Label, item.Label, path, item.Hint,
                [item.Label, item.Hint ?? "", .. choices]);

            // What sits inside a list (a column, a flag) is found by its own name, and leads to the list.
            IEnumerable<ChipSetting> inner = item switch
            {
                ChipGroupSetting chips => chips.Chips,
                ReorderListSetting list => list.Items.SelectMany(row => new[] { row.Visible, row.Companion }).OfType<ChipSetting>(),
                _ => [],
            };
            foreach (var chip in inner)
            {
                yield return new SearchEntry(page, tab, group.Title, item.Label, chip.Label, $"{path} › {item.Label}", chip.Hint,
                    [chip.Label, chip.Hint ?? ""]);
            }
        }
    }

    /// <summary>"POSITION &amp; VISIBILITY" as "Position &amp; visibility".</summary>
    private static string Capitalize(string title) =>
        title.Length == 0 ? title : title[..1] + title[1..].ToLowerInvariant();

    private void OpenResult(SearchEntry entry)
    {
        Navigate(entry.Page.Key, entry.Tab);
        if (entry.GroupTitle is null || _selected is not { } page)
        {
            return;
        }

        var group = page.Settings.FirstOrDefault(candidate => candidate.Title == entry.GroupTitle);
        var item = group?.Items.FirstOrDefault(candidate => candidate.Label == entry.SettingLabel);
        if (group is null || item is null)
        {
            return;
        }

        group.KeepOpen = true;
        Highlight(item);
        // After the page has been laid out, or there is nothing yet to scroll to.
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => RevealRequested?.Invoke(item));
    }

    /// <summary>Lights the row, and puts it out again a couple of seconds later.</summary>
    private void Highlight(SettingItem item)
    {
        if (_highlighted is not null)
        {
            _highlighted.IsHighlighted = false;
        }

        _highlighted = item;
        item.IsHighlighted = true;
        if (Application.Current is null)
        {
            return;
        }

        _highlightTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _highlightTimer.Stop();
        _highlightTimer.Tick -= OnHighlightElapsed;
        _highlightTimer.Tick += OnHighlightElapsed;
        _highlightTimer.Start();
    }

    private void OnHighlightElapsed(object? sender, EventArgs e)
    {
        _highlightTimer?.Stop();
        if (_highlighted is not null)
        {
            _highlighted.IsHighlighted = false;
            _highlighted = null;
        }
    }

    /// <summary>One searchable thing: a page, or an option on one, or something inside an option.</summary>
    private sealed record SearchEntry(
        NavItem Page,
        int Tab,
        string? GroupTitle,
        string? SettingLabel,
        string Title,
        string Path,
        string? Detail,
        IReadOnlyList<string> Terms)
    {
        public bool Matches(string term) => Terms.Any(text => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>A line in the search results: what was found, where it lives, and how to get there.</summary>
public sealed record SearchResult(string Title, string Path, string? Detail, string IconData, ICommand OpenCommand);
