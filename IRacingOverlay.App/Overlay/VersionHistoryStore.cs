using System.IO;
using IRacingOverlay.App.About;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists which versions have run on this PC, for the About page and the update notice.</summary>
internal static class VersionHistoryStore
{
    private const string FileName = "versions.json";

    private static readonly string FilePath = Path.Combine(TrayPreferencesStore.ConfigFolder, FileName);

    public static VersionHistory? Load() =>
        SettingsFile.ReadJson<VersionHistory>(FilePath) is { } history
            ? history with { Versions = history.Versions?.Where(version => version?.Version is not null).ToList() ?? [] }
            : null;

    public static void Save(VersionHistory history) => SettingsFile.WriteJson(FilePath, history);

    /// <summary>Whether an earlier run left settings behind. Every store writes into the same
    /// folder, so any of them counts.</summary>
    public static bool HasEarlierSettings()
    {
        var folder = TrayPreferencesStore.ConfigFolder;
        return Directory.Exists(folder) && Directory.EnumerateFiles(folder)
            .Select(Path.GetFileName)
            .Any(name => name is not null &&
                         !name.StartsWith(FileName, StringComparison.OrdinalIgnoreCase) &&
                         (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)));
    }
}
