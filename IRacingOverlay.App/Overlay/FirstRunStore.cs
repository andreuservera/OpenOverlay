using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>Whether the setup assistant has been offered on this PC. Marked as it opens, so it is
/// offered once, never again on its own: from then on it is on the Home page for whoever wants it.</summary>
internal static class FirstRunStore
{
    private sealed record State(bool SetupOffered);

    private static State? _cache;

    /// <summary>Settable so tests can point it at a temporary file.</summary>
    internal static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "first-run.json");

    public static bool SetupOffered => Load().SetupOffered;

    public static void MarkSetupOffered()
    {
        _cache = new State(true);
        SettingsFile.WriteJson(FilePath, _cache);
    }

    private static State Load() => _cache ??= SettingsFile.ReadJson<State>(FilePath) ?? new State(false);
}
