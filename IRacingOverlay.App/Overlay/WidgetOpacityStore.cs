using System.IO;
using System.Text.Json;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists each widget's opacity, keyed by widget name — same JSON-dictionary-per-key
/// shape as the neighbouring stores. Kept in its own file rather than folded into the scale store
/// because the two answer different questions ("how big" versus "how present") and are read at
/// different moments: scale on the panel's Loaded, opacity the instant the window exists.</summary>
internal static class WidgetOpacityStore
{
    /// <summary>What a widget is worth before anyone has touched the slider: fully solid, i.e. the
    /// behaviour every existing installation already has.</summary>
    public const double Default = 1.0;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "widget-opacity.json");

    private static Dictionary<string, double>? _cache;

    public static double Get(string widgetName)
    {
        Load();
        return _cache!.TryGetValue(widgetName, out var value) && value is >= 0 and <= 1
            ? value
            : Default;
    }

    public static void Save(string widgetName, double opacity)
    {
        Load();
        _cache![widgetName] = Math.Clamp(opacity, 0, 1);
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
