using System.IO;
using System.Text.Json;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists which overlay checkboxes were checked in the control panel, so the app restores
/// the same set of visible widgets on the next launch instead of starting with everything hidden.
/// Same JSON-file-per-key pattern as WidgetLayoutStore/PanelScaleStore, kept separate since "was this
/// checked" is conceptually distinct from position/size or font scale.</summary>
internal static class WidgetVisibilityStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "widget-visibility.json");

    private static Dictionary<string, bool>? _cache;

    public static bool Get(string widgetName, bool defaultValue = false)
    {
        Load();
        return _cache!.GetValueOrDefault(widgetName, defaultValue);
    }

    public static void Save(string widgetName, bool isVisible)
    {
        Load();
        _cache![widgetName] = isVisible;
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
