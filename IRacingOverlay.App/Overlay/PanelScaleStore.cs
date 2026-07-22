using System.IO;
using System.Text.Json;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists each panel's font/content scale across app restarts, keyed by a caller-chosen
/// name. Separate from <see cref="WidgetLayoutStore"/> since dashboard panels have a scale but no
/// position/size to persist, and floating widgets' scale is independent of their position/size.</summary>
internal static class PanelScaleStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "panel-scale.json");

    private static Dictionary<string, double>? _cache;

    public static double Get(string panelName, double defaultScale = 1.0)
    {
        Load();
        return _cache!.GetValueOrDefault(panelName, defaultScale);
    }

    public static void Save(string panelName, double scale)
    {
        Load();
        _cache![panelName] = scale;
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
                ? JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (JsonException)
        {
            _cache = [];
        }
    }
}
