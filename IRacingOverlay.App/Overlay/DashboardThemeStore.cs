using System.IO;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the chosen Dashboard theme across restarts — a single value, so a plain text
/// file rather than the JSON-dictionary pattern the other *Store classes use.</summary>
internal static class DashboardThemeStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "dashboard-theme.txt");

    public static DashboardTheme Get()
    {
        try
        {
            if (File.Exists(FilePath) && Enum.TryParse<DashboardTheme>(File.ReadAllText(FilePath).Trim(), out var theme))
            {
                return theme;
            }
        }
        catch (IOException)
        {
            // fall through to default
        }

        return DashboardTheme.Classic;
    }

    public static void Save(DashboardTheme theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, theme.ToString());
    }
}
