using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// Reads the wind compass refresh rate from where General › Performance used to keep it, so the
/// first run after it moved into the Weather widget's options carries the user's choice over. Never
/// written any more.
/// </summary>
internal static class CompassRefreshStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "compass-refresh.txt");

    /// <summary>The old choice in Hz: "Smooth (animated)" becomes the fastest rate; the fixed
    /// 10/5/2/1 Hz rates, and no choice at all, become the slowest one the new setting offers.</summary>
    public static int LegacyHz() =>
        SettingsFile.ReadText(FilePath) is { } text && int.TryParse(text.Trim(), out var index) && index == 0
            ? ViewModels.WeatherOptions.MaxCompassRefreshHz
            : ViewModels.WeatherOptions.MinCompassRefreshHz;
}
