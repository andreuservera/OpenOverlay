using System.IO;
using System.Text.Json;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the Weather widget's options as one JSON object, same convention as
/// FuelCalculatorOptionsStore.</summary>
internal static class WeatherOptionsStore
{
    private sealed record Snapshot(
        bool ShowAirTemp,
        bool ShowTrackTemp,
        bool ShowHumidity,
        bool ShowWindSpeed,
        bool ShowWindArrow,
        bool ShowForecast,
        bool ShowRainProbability,
        bool ShowLabels,
        bool Compact,
        WeatherUnits Units,
        WeatherGraphicSize IconSize,
        WeatherGraphicSize ArrowSize);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "weather.json");

    public static void ApplyTo(WeatherOptions options)
    {
        var saved = Load();
        if (saved is null)
        {
            return;
        }

        options.ShowAirTemp = saved.ShowAirTemp;
        options.ShowTrackTemp = saved.ShowTrackTemp;
        options.ShowHumidity = saved.ShowHumidity;
        options.ShowWindSpeed = saved.ShowWindSpeed;
        options.ShowWindArrow = saved.ShowWindArrow;
        options.ShowForecast = saved.ShowForecast;
        options.ShowRainProbability = saved.ShowRainProbability;
        options.ShowLabels = saved.ShowLabels;
        options.Compact = saved.Compact;
        options.Units = saved.Units;
        options.IconSize = saved.IconSize;
        options.ArrowSize = saved.ArrowSize;
    }

    public static void Save(WeatherOptions options)
    {
        var snapshot = new Snapshot(
            options.ShowAirTemp,
            options.ShowTrackTemp,
            options.ShowHumidity,
            options.ShowWindSpeed,
            options.ShowWindArrow,
            options.ShowForecast,
            options.ShowRainProbability,
            options.ShowLabels,
            options.Compact,
            options.Units,
            options.IconSize,
            options.ArrowSize);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot));
    }

    private static Snapshot? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(FilePath)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
