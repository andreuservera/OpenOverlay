using System.IO;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Overlay;

/// <summary>Persists the cockpit's modules and fixed elements as one JSON object, same convention as
/// PedalTraceOptionsStore. No file, or one without a module order, leaves the defaults; a module the
/// file doesn't name (added since) comes back last and hidden.</summary>
internal static class CockpitOptionsStore
{
    private sealed record Snapshot(
        // Module names left to right, and the ones shown.
        string[]? ModuleOrder = null,
        string[]? VisibleModules = null,
        bool ShowShiftLights = true,
        bool ShowProximityRadar = true);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IRacingOverlay", "cockpit.json");

    public static void ApplyTo(CockpitOptions options)
    {
        if (SettingsFile.ReadJson<Snapshot>(FilePath) is not { ModuleOrder: { } order } saved)
        {
            return;
        }

        options.Load(Modules(order), Modules(saved.VisibleModules ?? []), saved.ShowShiftLights, saved.ShowProximityRadar);
    }

    public static void Save(CockpitOptions options) =>
        SettingsFile.WriteJson(FilePath, new Snapshot(
            options.ModuleOrder.Select(module => module.ToString()).ToArray(),
            options.VisibleModules().Select(module => module.ToString()).ToArray(),
            options.ShowShiftLights,
            options.ShowProximityRadar));

    private static IEnumerable<CockpitModule> Modules(IEnumerable<string> names) => names
        .Select(name => Enum.TryParse<CockpitModule>(name, out var module) && Enum.IsDefined(module) ? module : (CockpitModule?)null)
        .OfType<CockpitModule>();
}
