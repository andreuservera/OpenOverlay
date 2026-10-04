using System.IO;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists each driver table's column toggles and focus size, same JSON-file-per-key
/// pattern as WidgetVisibilityStore. Keys are prefixed by table, so Standings and Relative keep
/// independent settings in the one file.</summary>
internal static class DriverTableOptionsStore
{
    private const string FocusSizeKey = "FocusSize";
    // Stored under its original name, so the setting survives the car-name → class-name change.
    private const string ShowClassNameKey = "ShowCarName";
    private const string ShowSofKey = "ShowSof";
    private const string ShowColumnHeadersKey = "ShowColumnHeaders";
    private const string ShowMulticlassKey = "ShowMulticlass";
    private const string ShowSessionLapsKey = "ShowSessionLaps";
    // Each column's place in the order, under "Order.<Column>": the file holds ints, so the order
    // is kept as one index per column rather than as a list.
    private const string OrderKeyPrefix = "Order.";
    // Where each piece of session information sits around the table, under "Slot.<Element>".
    private const string SlotKeyPrefix = "Slot.";
    // Shown or not, for elements added after the first four (which keep their own keys).
    private const string ShowKeyPrefix = "Show.";
    private const string ShowClassDriversKey = "ShowClassDrivers";
    private const string ShowClassSofKey = "ShowClassSof";
    private const string ShowSessionTimeKey = "ShowSessionTime";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "driver-tables.json");

    // Values are ints rather than bools because the same file also carries the focus size.
    // Booleans round-trip as 0/1.
    private static Dictionary<string, int>? _cache;

    public static void SaveColumn(DriverTable table, DriverTableColumn column, bool isVisible) =>
        Set(table, column.ToString(), isVisible ? 1 : 0);

    public static void SaveClassName(DriverTable table, bool isVisible) =>
        Set(table, ShowClassNameKey, isVisible ? 1 : 0);

    public static void SaveSof(DriverTable table, bool isVisible) =>
        Set(table, ShowSofKey, isVisible ? 1 : 0);

    public static void SaveColumnHeaders(DriverTable table, bool isVisible) =>
        Set(table, ShowColumnHeadersKey, isVisible ? 1 : 0);

    public static void SaveMulticlass(DriverTable table, bool isEnabled) =>
        Set(table, ShowMulticlassKey, isEnabled ? 1 : 0);

    public static void SaveSessionLaps(DriverTable table, bool isVisible) =>
        Set(table, ShowSessionLapsKey, isVisible ? 1 : 0);

    public static void SaveSessionTime(DriverTable table, bool isVisible) =>
        Set(table, ShowSessionTimeKey, isVisible ? 1 : 0);

    public static void SaveFocusSize(DriverTable table, int value) => Set(table, FocusSizeKey, value);

    public static void SaveInfoSlot(DriverTable table, TableInfoElement element, TableSlot slot) =>
        Set(table, SlotKeyPrefix + element, (int)slot);

    /// <summary>Whether an element shows, under the key it has always had.</summary>
    public static void SaveInfoShown(DriverTable table, TableInfoElement element, bool shown)
    {
        switch (element)
        {
            case TableInfoElement.SessionType: SaveClassName(table, shown); break;
            case TableInfoElement.Sof: SaveSof(table, shown); break;
            case TableInfoElement.SessionLaps: SaveSessionLaps(table, shown); break;
            case TableInfoElement.SessionTime: SaveSessionTime(table, shown); break;
            default: Set(table, ShowKeyPrefix + element, shown ? 1 : 0); break;
        }
    }

    public static void SaveClassDrivers(DriverTable table, bool isVisible) => Set(table, ShowClassDriversKey, isVisible ? 1 : 0);

    public static void SaveClassSof(DriverTable table, bool isVisible) => Set(table, ShowClassSofKey, isVisible ? 1 : 0);

    public static void SaveColumnOrder(DriverTable table, IReadOnlyList<DriverTableColumn> order)
    {
        Load();
        WriteOrder(table, order);
        SettingsFile.WriteJson(FilePath, _cache);
    }

    /// <summary>Every setting of one table in a single write, under the same keys the per-setting
    /// saves use — for when a whole set changes at once, as when a layout is applied.</summary>
    public static void Save(DriverTableOptions options)
    {
        Load();
        foreach (var column in Enum.GetValues<DriverTableColumn>())
        {
            _cache![Key(options.Table, column.ToString())] = options.IsVisible(column) ? 1 : 0;
        }

        _cache![Key(options.Table, ShowClassNameKey)] = options.ShowClassName ? 1 : 0;
        _cache[Key(options.Table, ShowSofKey)] = options.ShowSof ? 1 : 0;
        _cache[Key(options.Table, ShowColumnHeadersKey)] = options.ShowColumnHeaders ? 1 : 0;
        _cache[Key(options.Table, ShowMulticlassKey)] = options.ShowMulticlass ? 1 : 0;
        _cache[Key(options.Table, ShowSessionLapsKey)] = options.ShowSessionLaps ? 1 : 0;
        _cache[Key(options.Table, ShowSessionTimeKey)] = options.ShowSessionTime ? 1 : 0;
        _cache[Key(options.Table, FocusSizeKey)] = options.FocusSize;
        WriteOrder(options.Table, options.ColumnOrder);
        foreach (var element in Enum.GetValues<TableInfoElement>())
        {
            _cache[Key(options.Table, SlotKeyPrefix + element)] = (int)options.SlotOf(element);
            if (!DriverTableOptions.HasOwnSwitch(element))
            {
                _cache[Key(options.Table, ShowKeyPrefix + element)] = options.IsShown(element) ? 1 : 0;
            }
        }

        _cache[Key(options.Table, ShowClassDriversKey)] = options.ShowClassDrivers ? 1 : 0;
        _cache[Key(options.Table, ShowClassSofKey)] = options.ShowClassSof ? 1 : 0;

        SettingsFile.WriteJson(FilePath, _cache);
    }

    private static string Key(DriverTable table, string key) => $"{table}.{key}";

    public static void ApplyTo(DriverTableOptions options)
    {
        Load();
        foreach (var column in Enum.GetValues<DriverTableColumn>())
        {
            var defaultVisible = column is DriverTableColumn.LastPitStop or DriverTableColumn.CarBrand ? 0 : 1;
            options.SetVisible(column, Get(options.Table, column.ToString(), defaultVisible) != 0);
        }

        options.ShowClassName = Get(options.Table, ShowClassNameKey, 1) != 0;
        options.ShowSof = Get(options.Table, ShowSofKey, 1) != 0;
        options.ShowColumnHeaders = Get(options.Table, ShowColumnHeadersKey, 1) != 0;
        options.ShowMulticlass = Get(options.Table, ShowMulticlassKey, 1) != 0;
        options.ShowSessionLaps = Get(options.Table, ShowSessionLapsKey, 1) != 0;
        options.ShowSessionTime = Get(options.Table, ShowSessionTimeKey, 1) != 0;
        options.ColumnOrder = ReadOrder(options.Table);
        foreach (var element in Enum.GetValues<TableInfoElement>())
        {
            options.SetSlot(element, (TableSlot)Get(options.Table, SlotKeyPrefix + element, (int)DriverTableOptions.DefaultSlot(element)));
            if (!DriverTableOptions.HasOwnSwitch(element))
            {
                options.SetShown(element, Get(options.Table, ShowKeyPrefix + element, 0) != 0);
            }
        }

        options.ShowClassDrivers = Get(options.Table, ShowClassDriversKey, 0) != 0;
        options.ShowClassSof = Get(options.Table, ShowClassSofKey, 0) != 0;

        options.FocusSize = Get(options.Table, FocusSizeKey, options.Table == DriverTable.Standings
            ? DriverTableOptions.DefaultStandingsFocusSize
            : DriverTableOptions.DefaultRelativeFocusSize);
    }

    private static void WriteOrder(DriverTable table, IReadOnlyList<DriverTableColumn> order)
    {
        for (var i = 0; i < order.Count; i++)
        {
            _cache![Key(table, OrderKeyPrefix + order[i])] = i;
        }
    }

    /// <summary>The saved order; a column with no saved place (never reordered, or new since) keeps
    /// its default place, after any saved ones it ties with.</summary>
    private static IReadOnlyList<DriverTableColumn> ReadOrder(DriverTable table) =>
        DriverTableColumnLayout.DefaultOrder
            .Select((column, defaultIndex) => (Column: column, Index: Get(table, OrderKeyPrefix + column, defaultIndex), Fallback: defaultIndex))
            .OrderBy(entry => entry.Index)
            .ThenBy(entry => entry.Fallback)
            .Select(entry => entry.Column)
            .ToList();

    private static int Get(DriverTable table, string key, int defaultValue)
    {
        Load();
        return _cache!.GetValueOrDefault($"{table}.{key}", defaultValue);
    }

    private static void Set(DriverTable table, string key, int value)
    {
        Load();
        _cache![$"{table}.{key}"] = value;
        SettingsFile.WriteJson(FilePath, _cache);
    }

    private static void Load()
    {
        _cache ??= SettingsFile.ReadJson<Dictionary<string, int>>(FilePath) ?? [];
    }
}
