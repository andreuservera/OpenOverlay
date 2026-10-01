using System.IO;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists each driver table's column toggles and focus size, same JSON-file-per-key
/// pattern as WidgetVisibilityStore. Keys are prefixed by table, so Standings and Relative keep
/// independent settings in the one file.</summary>
internal static class DriverTableOptionsStore
{
    private const string FocusSizeKey = "FocusSize";
    private const string ShowSessionIdKey = "ShowSessionId";
    // Stored under its original name, so the setting survives the car-name → class-name change.
    private const string ShowClassNameKey = "ShowCarName";
    private const string ShowMulticlassKey = "ShowMulticlass";
    private const string ShowSessionLapsKey = "ShowSessionLaps";
    private const string ShowSessionTimeKey = "ShowSessionTime";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "driver-tables.json");

    // Values are ints rather than bools because the same file also carries the focus size.
    // Booleans round-trip as 0/1.
    private static Dictionary<string, int>? _cache;

    public static void SaveColumn(DriverTable table, DriverTableColumn column, bool isVisible) =>
        Set(table, column.ToString(), isVisible ? 1 : 0);

    public static void SaveSessionId(DriverTable table, bool isVisible) =>
        Set(table, ShowSessionIdKey, isVisible ? 1 : 0);

    public static void SaveClassName(DriverTable table, bool isVisible) =>
        Set(table, ShowClassNameKey, isVisible ? 1 : 0);

    public static void SaveMulticlass(DriverTable table, bool isEnabled) =>
        Set(table, ShowMulticlassKey, isEnabled ? 1 : 0);

    public static void SaveSessionLaps(DriverTable table, bool isVisible) =>
        Set(table, ShowSessionLapsKey, isVisible ? 1 : 0);

    public static void SaveSessionTime(DriverTable table, bool isVisible) =>
        Set(table, ShowSessionTimeKey, isVisible ? 1 : 0);

    public static void SaveFocusSize(DriverTable table, int value) => Set(table, FocusSizeKey, value);

    public static void ApplyTo(DriverTableOptions options)
    {
        Load();
        foreach (var column in Enum.GetValues<DriverTableColumn>())
        {
            var defaultVisible = column == DriverTableColumn.LastPitStop ? 0 : 1;
            options.SetVisible(column, Get(options.Table, column.ToString(), defaultVisible) != 0);
        }

        options.ShowSessionId = Get(options.Table, ShowSessionIdKey, 0) != 0;
        options.ShowClassName = Get(options.Table, ShowClassNameKey, 1) != 0;
        options.ShowMulticlass = Get(options.Table, ShowMulticlassKey, 1) != 0;
        options.ShowSessionLaps = Get(options.Table, ShowSessionLapsKey, 1) != 0;
        options.ShowSessionTime = Get(options.Table, ShowSessionTimeKey, 1) != 0;
        options.FocusSize = Get(options.Table, FocusSizeKey, options.Table == DriverTable.Standings
            ? DriverTableOptions.DefaultStandingsFocusSize
            : DriverTableOptions.DefaultRelativeFocusSize);
    }

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
