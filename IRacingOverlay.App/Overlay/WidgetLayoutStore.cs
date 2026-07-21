using System.IO;
using System.Text.Json;

namespace IRacingOverlay.App.Overlay;

internal sealed record WidgetLayout(double Left, double Top, double Width, double Height);

/// <summary>Persists each widget's position/size across app restarts, keyed by widget name.</summary>
internal static class WidgetLayoutStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "layout.json");

    private static Dictionary<string, WidgetLayout>? _cache;

    public static WidgetLayout? Get(string widgetName)
    {
        Load();
        return _cache!.GetValueOrDefault(widgetName);
    }

    public static void Save(string widgetName, WidgetLayout layout)
    {
        Load();
        _cache![widgetName] = layout;
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
                ? JsonSerializer.Deserialize<Dictionary<string, WidgetLayout>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (JsonException)
        {
            _cache = [];
        }
    }
}
