using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the Weather wind compass's refresh-rate index, same format as CriticalRefreshStore.</summary>
internal static class CompassRefreshStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "compass-refresh.txt");

    public static int Get(int defaultIndex = 3) =>
        SettingsFile.ReadText(FilePath) is { } text && int.TryParse(text.Trim(), out var index) && index is >= 0 and <= 4
            ? index
            : defaultIndex;

    public static void Save(int index) => SettingsFile.Write(FilePath, index.ToString());
}
