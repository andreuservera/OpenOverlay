using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>Where the control panel's splitter between the settings and the preview was left, as
/// the settings' share of the two panes: 40 % unless moved.</summary>
internal static class PaneSplitStore
{
    private sealed record State(double ConfigShare);

    public const double DefaultShare = 0.4;

    private static State? _cache;

    /// <summary>Settable so tests can point it at a temporary file.</summary>
    internal static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "pane-split.json");

    public static double ConfigShare
    {
        get
        {
            var saved = (_cache ??= SettingsFile.ReadJson<State>(FilePath) ?? new State(DefaultShare)).ConfigShare;
            return saved is >= 0.1 and <= 0.9 ? saved : DefaultShare;
        }
    }

    public static void Save(double configShare)
    {
        _cache = new State(Math.Clamp(configShare, 0.1, 0.9));
        SettingsFile.WriteJson(FilePath, _cache);
    }

    /// <summary>Forgets what was read, so the next call reads <see cref="FilePath"/> again.</summary>
    internal static void Reset() => _cache = null;
}
