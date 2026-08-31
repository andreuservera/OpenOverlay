using System.IO;
using System.Text.Json;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists each panel's <see cref="ScaleLevel"/> across app restarts, keyed by a
/// caller-chosen name. Separate from <see cref="WidgetLayoutStore"/> since dashboard panels have a
/// scale but no position to persist, and a floating widget's scale is independent of where it sits.
/// Levels are stored by name ("M", "XL"), never as multipliers, so the ladder's factors can be
/// retuned later without silently reinterpreting everyone's saved sizes.</summary>
internal static class ScaleLevelStore
{
    // Deliberately not the old "panel-scale.json": that file holds free-form multipliers from the
    // drag-to-resize era, and a stale 2.5 there must not resurrect a size the ladder no longer has.
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "panel-scale-level.json");

    private static Dictionary<string, string>? _cache;

    public static ScaleLevel Get(string panelName)
    {
        Load();
        return ScaleLevels.Parse(_cache!.GetValueOrDefault(panelName));
    }

    public static void Save(string panelName, ScaleLevel level)
    {
        Load();
        _cache![panelName] = level.ToString();
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
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (JsonException)
        {
            _cache = [];
        }
    }
}
