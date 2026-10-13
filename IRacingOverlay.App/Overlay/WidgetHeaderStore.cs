using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists whether each widget shows its header strip (name and live dot), keyed by
/// widget name — the same JSON-dictionary-per-key shape as <see cref="WidgetOpacityStore"/>.</summary>
internal static class WidgetHeaderStore
{
    /// <summary>On, as the widget style shows it, for whatever has no default of its own (a panel
    /// drawn outside any widget window).</summary>
    public const bool Default = true;

    /// <summary>What a widget starts with: on for most, off for the bars and one-number readouts
    /// (see <see cref="ControlPanel.WidgetDescriptor.HeaderByDefault"/>).</summary>
    public static bool DefaultFor(string widgetName) => ControlPanel.WidgetCatalog.HeaderByDefault(widgetName);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "widget-header.json");

    private static Dictionary<string, bool>? _cache;

    public static bool Get(string widgetName)
    {
        Load();
        return _cache!.TryGetValue(widgetName, out var value) ? value : DefaultFor(widgetName);
    }

    public static void Save(string widgetName, bool show)
    {
        Load();
        _cache![widgetName] = show;
        SettingsFile.WriteJson(FilePath, _cache);
    }

    private static void Load()
    {
        _cache ??= SettingsFile.ReadJson<Dictionary<string, bool>>(FilePath) ?? [];
    }
}
