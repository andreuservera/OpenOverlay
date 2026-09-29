using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the chosen critical-refresh-rate ComboBox index across restarts — a single
/// value, so a plain text file rather than the JSON-dictionary pattern the other *Store classes use.</summary>
internal static class CriticalRefreshStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "critical-refresh.txt");

    public static int Get(int defaultIndex = 2) =>
        SettingsFile.ReadText(FilePath) is { } text && int.TryParse(text.Trim(), out var index) && index is >= 0 and <= 4
            ? index
            : defaultIndex;

    public static void Save(int index) => SettingsFile.Write(FilePath, index.ToString());
}
