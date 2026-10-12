using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists each panel's <see cref="WidgetScale"/> across app restarts, keyed by a
/// caller-chosen name. Separate from <see cref="WidgetLayoutStore"/> since dashboard panels have a
/// scale but no position to persist, and a floating widget's scale is independent of where it sits.
/// Stored as the percentage; an old size name ("XL") still reads as the percentage it was.</summary>
internal static class ScaleLevelStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "panel-scale-level.json");

    private static Dictionary<string, string>? _cache;

    public static WidgetScale Get(string panelName)
    {
        Load();
        return WidgetScale.Parse(_cache!.GetValueOrDefault(panelName));
    }

    public static void Save(string panelName, WidgetScale scale)
    {
        Load();
        _cache![panelName] = scale.ToString();
        SettingsFile.WriteJson(FilePath, _cache);
    }

    private static void Load()
    {
        _cache ??= SettingsFile.ReadJson<Dictionary<string, string>>(FilePath) ?? [];
    }
}
