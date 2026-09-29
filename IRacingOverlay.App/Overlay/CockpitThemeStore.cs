using System.IO;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the chosen cockpit theme — a single value, so a plain text file like
/// <see cref="DashboardThemeStore"/>.</summary>
internal static class CockpitThemeStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "cockpit-theme.txt");

    public static CockpitTheme Get()
    {
        try
        {
            if (File.Exists(FilePath) && Enum.TryParse<CockpitTheme>(File.ReadAllText(FilePath).Trim(), out var theme))
            {
                return theme;
            }
        }
        catch (IOException)
        {
            // fall through to default
        }

        return CockpitTheme.Default;
    }

    public static void Save(CockpitTheme theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, theme.ToString());
    }
}
