using System.IO;
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
        WeatherGraphicSize IconSize,
        WeatherGraphicSize ArrowSize,
        // Added later: files written before it have no value, and those users get it on.
        bool ShowTrackWetness = true,
        // Added later, when it moved here from General › Performance: 0 means not saved yet.
        int CompassRefreshHz = 0);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "weather.json");

    public static void ApplyTo(WeatherOptions options)
    {
        var saved = Load();
        options.CompassRefreshHz = saved is { CompassRefreshHz: > 0 } ? saved.CompassRefreshHz : CompassRefreshStore.LegacyHz();
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
        options.ShowTrackWetness = saved.ShowTrackWetness;
        options.ShowLabels = saved.ShowLabels;
        options.Compact = saved.Compact;
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
            options.IconSize,
            options.ArrowSize,
            options.ShowTrackWetness,
            options.CompassRefreshHz);

        SettingsFile.WriteJson(FilePath, snapshot);
    }

    private static Snapshot? Load() => SettingsFile.ReadJson<Snapshot>(FilePath);
}
