using System.IO;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the Pedal trace widget's options as one JSON object, same convention as
/// TrackInfoOptionsStore. A block missing from the file (one added later) comes back shown.</summary>
internal static class PedalTraceOptionsStore
{
    private sealed record Snapshot(
        bool ShowGear = true,
        bool ShowSpeed = true,
        bool ShowSteering = true,
        bool ShowTrace = true,
        bool ShowPedals = true,
        // Block names left to right; missing keeps the default order.
        string[]? ElementOrder = null);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "pedal-trace.json");

    public static void ApplyTo(PedalTraceOptions options)
    {
        if (SettingsFile.ReadJson<Snapshot>(FilePath) is not { } saved)
        {
            return;
        }

        options.ShowGear = saved.ShowGear;
        options.ShowSpeed = saved.ShowSpeed;
        options.ShowSteering = saved.ShowSteering;
        options.ShowTrace = saved.ShowTrace;
        options.ShowPedals = saved.ShowPedals;
        if (saved.ElementOrder is { } order)
        {
            options.ElementOrder = order
                .Select(name => Enum.TryParse<PedalTraceElement>(name, out var element) ? element : (PedalTraceElement?)null)
                .OfType<PedalTraceElement>()
                .ToList();
        }
    }

    public static void Save(PedalTraceOptions options) =>
        SettingsFile.WriteJson(FilePath, new Snapshot(
            options.ShowGear,
            options.ShowSpeed,
            options.ShowSteering,
            options.ShowTrace,
            options.ShowPedals,
            options.ElementOrder.Select(element => element.ToString()).ToArray()));
}
