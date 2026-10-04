using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// Which sections of the control panel and the layout editor are folded shut: the rail's
/// WIDGETS / APPLICATION / ABOUT, and each page's settings groups. One key per folded section;
/// anything not listed is open, so a section added later starts open.
/// </summary>
internal static class CollapseStore
{
    private static HashSet<string>? _cache;

    /// <summary>Settable so tests can point it at a temporary file.</summary>
    internal static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "collapsed-sections.json");

    public static bool IsCollapsed(string key) => Load().Contains(key);

    public static void SetCollapsed(string key, bool collapsed)
    {
        var keys = Load();
        if (collapsed ? keys.Add(key) : keys.Remove(key))
        {
            SettingsFile.WriteJson(FilePath, keys.Order(StringComparer.Ordinal).ToList());
        }
    }

    /// <summary>Forgets what was read, so the next call reads <see cref="FilePath"/> again.</summary>
    internal static void Reset() => _cache = null;

    private static HashSet<string> Load() =>
        _cache ??= new HashSet<string>(SettingsFile.ReadJson<List<string>>(FilePath) ?? [], StringComparer.Ordinal);
}
