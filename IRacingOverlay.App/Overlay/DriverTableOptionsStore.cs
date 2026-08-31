using System.IO;
using System.Text.Json;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists each driver table's column toggles and focus size, same JSON-file-per-key
/// pattern as WidgetVisibilityStore. Keys are prefixed by table, so Standings and Relative keep
/// independent settings in the one file.</summary>
internal static class DriverTableOptionsStore
{
    private const string FocusSizeKey = "FocusSize";
    private const string ShowSessionIdKey = "ShowSessionId";
    private const string ShowCarNameKey = "ShowCarName";
    private const string ShowMulticlassKey = "ShowMulticlass";

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

    public static void SaveCarName(DriverTable table, bool isVisible) =>
        Set(table, ShowCarNameKey, isVisible ? 1 : 0);

    public static void SaveMulticlass(DriverTable table, bool isEnabled) =>
        Set(table, ShowMulticlassKey, isEnabled ? 1 : 0);

    public static void SaveFocusSize(DriverTable table, int value) => Set(table, FocusSizeKey, value);

    public static void ApplyTo(DriverTableOptions options)
    {
        Load();
        foreach (var column in Enum.GetValues<DriverTableColumn>())
        {
            options.SetVisible(column, Get(options.Table, column.ToString(), 1) != 0);
        }

        options.ShowSessionId = Get(options.Table, ShowSessionIdKey, 0) != 0;
        options.ShowCarName = Get(options.Table, ShowCarNameKey, 1) != 0;
        options.ShowMulticlass = Get(options.Table, ShowMulticlassKey, 1) != 0;
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
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_cache));
    }

    private static void Load()
    {
        if (_cache is not null)
        {
            return;
        }

        try
        {
            _cache = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (JsonException)
        {
            _cache = [];
        }
    }
}
