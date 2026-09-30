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

    public static CockpitTheme Get() =>
        SettingsFile.ReadText(FilePath) is { } text && Enum.TryParse<CockpitTheme>(text.Trim(), out var theme)
            ? theme
            : CockpitTheme.Default;

    public static void Save(CockpitTheme theme) => SettingsFile.Write(FilePath, theme.ToString());
}
