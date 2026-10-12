using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the chosen language as its code, or "auto" to follow Windows.</summary>
internal static class LanguagePreferenceStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "language.txt");

    public static string Get() => SettingsFile.ReadText(FilePath)?.Trim() is { Length: > 0 } code ? code : Loc.Automatic;

    public static void Save(string preference) => SettingsFile.Write(FilePath, preference);
}
