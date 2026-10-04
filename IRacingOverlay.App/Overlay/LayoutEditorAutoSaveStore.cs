using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>Whether the layout editor saves as you go — one switch for every layout, kept across
/// restarts as a plain text file like CriticalRefreshStore.</summary>
internal static class LayoutEditorAutoSaveStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "layout-editor-autosave.txt");

    public static bool Get() => SettingsFile.ReadText(FilePath)?.Trim() == "1";

    public static void Save(bool enabled) => SettingsFile.Write(FilePath, enabled ? "1" : "0");
}
