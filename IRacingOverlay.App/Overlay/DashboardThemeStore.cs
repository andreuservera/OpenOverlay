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

    public static DashboardTheme Get() =>
        SettingsFile.ReadText(FilePath) is { } text && Enum.TryParse<DashboardTheme>(text.Trim(), out var theme)
            ? theme
            : DashboardTheme.Classic;

    public static void Save(DashboardTheme theme) => SettingsFile.Write(FilePath, theme.ToString());
}
