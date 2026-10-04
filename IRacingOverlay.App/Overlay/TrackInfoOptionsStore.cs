using System.IO;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the Track &amp; session widget's options as one JSON object, same convention as
/// WeatherOptionsStore. A field missing from the file (one added later) comes back shown.</summary>
internal static class TrackInfoOptionsStore
{
    private sealed record Snapshot(
        bool ShowTrackName = true,
        bool ShowSession = true,
        bool ShowAirTemp = true,
        bool ShowTrackTemp = true,
        bool ShowWind = true,
        bool ShowHumidity = true,
        bool ShowTrackUsage = true,
        bool ShowTimeLeft = true,
        bool ShowLap = true);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "track-info.json");

    public static void ApplyTo(TrackInfoOptions options)
    {
        if (SettingsFile.ReadJson<Snapshot>(FilePath) is not { } saved)
        {
            return;
        }

        options.ShowTrackName = saved.ShowTrackName;
        options.ShowSession = saved.ShowSession;
        options.ShowAirTemp = saved.ShowAirTemp;
        options.ShowTrackTemp = saved.ShowTrackTemp;
        options.ShowWind = saved.ShowWind;
        options.ShowHumidity = saved.ShowHumidity;
        options.ShowTrackUsage = saved.ShowTrackUsage;
        options.ShowTimeLeft = saved.ShowTimeLeft;
        options.ShowLap = saved.ShowLap;
    }

    public static void Save(TrackInfoOptions options) =>
        SettingsFile.WriteJson(FilePath, new Snapshot(
            options.ShowTrackName,
            options.ShowSession,
            options.ShowAirTemp,
            options.ShowTrackTemp,
            options.ShowWind,
            options.ShowHumidity,
            options.ShowTrackUsage,
            options.ShowTimeLeft,
            options.ShowLap));
}
