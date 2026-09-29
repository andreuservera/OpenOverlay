using System.IO;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the unit preference (follow iRacing / metric / imperial) as a single name.</summary>
internal static class UnitPreferenceStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "units.txt");

    public static UnitPreference Get()
    {
        try
        {
            if (File.Exists(FilePath) && Enum.TryParse<UnitPreference>(File.ReadAllText(FilePath).Trim(), out var preference))
            {
                return preference;
            }
        }
        catch (IOException)
        {
            // fall through to default
        }

        return UnitPreference.FollowIRacing;
    }

    public static void Save(UnitPreference preference)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, preference.ToString());
    }
}
