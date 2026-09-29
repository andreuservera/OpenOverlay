using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists each widget's "hide whenever I'm not actually driving" toggle, keyed by widget
/// name — same JSON-dictionary-per-key shape as WidgetVisibilityStore, kept as a separate file since
/// "should this widget auto-hide outside the car" is conceptually distinct from "is this widget
/// enabled at all."</summary>
internal static class HideOutsideCarStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "hide-outside-car.json");

    private static Dictionary<string, bool>? _cache;

    public static bool Get(string widgetName, bool defaultValue = false)
    {
        Load();
        return _cache!.GetValueOrDefault(widgetName, defaultValue);
    }

    public static void Save(string widgetName, bool hideOutsideCar)
    {
        Load();
        _cache![widgetName] = hideOutsideCar;
        SettingsFile.WriteJson(FilePath, _cache);
    }

    private static void Load()
    {
        _cache ??= SettingsFile.ReadJson<Dictionary<string, bool>>(FilePath) ?? [];
    }
}
