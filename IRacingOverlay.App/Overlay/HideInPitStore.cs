using System.IO;
using System.Text.Json;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists each widget's "hide while on pit road" toggle, keyed by widget name — same
/// JSON-dictionary-per-key shape as WidgetVisibilityStore, kept as a separate file since "should this
/// widget hide in the pits" is conceptually distinct from "is this widget enabled at all."</summary>
internal static class HideInPitStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "hide-in-pit.json");

    private static Dictionary<string, bool>? _cache;

    public static bool Get(string widgetName, bool defaultValue = false)
    {
        Load();
        return _cache!.GetValueOrDefault(widgetName, defaultValue);
    }

    public static void Save(string widgetName, bool hideInPit)
    {
        Load();
        _cache![widgetName] = hideInPit;
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
                ? JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (JsonException)
        {
            _cache = [];
        }
    }
}
