using System.IO;
using System.Text.Json;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the Standings widget's optional-column checkboxes, same JSON-file-per-key
/// pattern as WidgetVisibilityStore. Kept separate since this is specific to one widget's internal
/// display, not "is the widget shown at all."</summary>
internal static class StandingsColumnVisibilityStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "standings-columns.json");

    private static Dictionary<string, bool>? _cache;

    public static bool Get(string columnName, bool defaultValue = true)
    {
        Load();
        return _cache!.GetValueOrDefault(columnName, defaultValue);
    }

    public static void Save(string columnName, bool isVisible)
    {
        Load();
        _cache![columnName] = isVisible;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_cache));
    }

    public static void ApplyTo(StandingsColumnVisibility visibility)
    {
        visibility.ShowIRating = Get(nameof(StandingsColumnVisibility.ShowIRating));
        visibility.ShowIRatingDelta = Get(nameof(StandingsColumnVisibility.ShowIRatingDelta));
        visibility.ShowLicense = Get(nameof(StandingsColumnVisibility.ShowLicense));
        visibility.ShowLap = Get(nameof(StandingsColumnVisibility.ShowLap));
        visibility.ShowLastLap = Get(nameof(StandingsColumnVisibility.ShowLastLap));
        visibility.ShowBestLap = Get(nameof(StandingsColumnVisibility.ShowBestLap));
        visibility.ShowGap = Get(nameof(StandingsColumnVisibility.ShowGap));
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
