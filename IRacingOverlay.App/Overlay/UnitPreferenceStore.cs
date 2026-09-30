using System.IO;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the unit preference (follow iRacing / metric / imperial) as a single name.</summary>
internal static class UnitPreferenceStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "units.txt");

    public static UnitPreference Get() =>
        SettingsFile.ReadText(FilePath) is { } text && Enum.TryParse<UnitPreference>(text.Trim(), out var preference)
            ? preference
            : UnitPreference.FollowIRacing;

    public static void Save(UnitPreference preference) => SettingsFile.Write(FilePath, preference.ToString());
}
