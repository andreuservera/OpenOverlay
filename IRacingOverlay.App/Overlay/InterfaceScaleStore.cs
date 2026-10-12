using System.IO;

namespace IRacingOverlay.App.Overlay;

/// <summary>How big the control panel draws itself: one of <see cref="Steps"/>, 100 % unless chosen.
/// Only the control panel: the widgets have their own size per widget.</summary>
internal static class InterfaceScaleStore
{
    private sealed record State(double Scale);

    /// <summary>The sizes offered, as factors.</summary>
    public static readonly double[] Steps = [0.9, 1.0, 1.1, 1.25, 1.5];

    private static State? _cache;

    /// <summary>Settable so tests can point it at a temporary file.</summary>
    internal static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "interface-scale.json");

    public static double Scale
    {
        get
        {
            var saved = (_cache ??= SettingsFile.ReadJson<State>(FilePath) ?? new State(1.0)).Scale;
            return Steps.Contains(saved) ? saved : 1.0;
        }
    }

    public static void Save(double scale)
    {
        _cache = new State(scale);
        SettingsFile.WriteJson(FilePath, _cache);
    }
}
