using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the chosen critical-refresh-rate ComboBox index across restarts — a single
/// value, so a plain text file rather than the JSON-dictionary pattern the other *Store classes use.</summary>
internal static class CriticalRefreshStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "critical-refresh.txt");

    public static int Get(int defaultIndex = 2)
    {
        try
        {
            if (File.Exists(FilePath) && int.TryParse(File.ReadAllText(FilePath).Trim(), out var index) && index is >= 0 and <= 4)
            {
                return index;
            }
        }
        catch (IOException)
        {
            // fall through to default
        }

        return defaultIndex;
    }

    public static void Save(int index)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, index.ToString());
    }
}
