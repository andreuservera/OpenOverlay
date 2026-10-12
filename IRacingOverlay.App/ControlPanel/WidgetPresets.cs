using System.Windows;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>A starting set of widgets, for someone who would rather not think about twelve of them.</summary>
public sealed record WidgetPreset(string Name, string Description, IReadOnlyList<string> Widgets);

/// <summary>
/// The setup assistant's presets, and where each widget starts on the monitor iRacing runs on: the
/// timing tables down the left, the car along the bottom middle, flags and delta top centre, the
/// session and weather top right, strategy down the right — out of the way of the racing line and
/// the mirror, which is where people end up putting them anyway.
/// </summary>
public static class WidgetPresets
{
    public static IReadOnlyList<WidgetPreset> All { get; } =
    [
        new("Essential",
            "The three most drivers race with: who is around you, your car, and the flags.",
            [WidgetCatalog.Relative, WidgetCatalog.Cockpit, WidgetCatalog.Flag]),
        new("Complete",
            "A full race weekend: timing, the map, your car, tyres, fuel and the session.",
            [WidgetCatalog.Relative, WidgetCatalog.Standings, WidgetCatalog.TrackMap, WidgetCatalog.Flag,
             WidgetCatalog.Cockpit, WidgetCatalog.TireInfo, WidgetCatalog.FuelCalculator, WidgetCatalog.Delta,
             WidgetCatalog.TrackInfo]),
        new("Streamer",
            "What viewers follow: the running order, the map, flags, incidents and the conditions.",
            [WidgetCatalog.Relative, WidgetCatalog.Standings, WidgetCatalog.TrackMap, WidgetCatalog.Flag,
             WidgetCatalog.Incident, WidgetCatalog.TrackInfo, WidgetCatalog.Weather]),
    ];

    // Where each widget's top-left corner starts, as a fraction of the monitor.
    private static readonly Dictionary<string, (double X, double Y)> Spots = new()
    {
        [WidgetCatalog.Standings] = (0.02, 0.05),
        [WidgetCatalog.Relative] = (0.02, 0.42),
        [WidgetCatalog.TrackMap] = (0.30, 0.90),
        [WidgetCatalog.Flag] = (0.42, 0.04),
        [WidgetCatalog.Delta] = (0.44, 0.17),
        [WidgetCatalog.Incident] = (0.60, 0.04),
        [WidgetCatalog.TrackInfo] = (0.72, 0.03),
        [WidgetCatalog.Weather] = (0.88, 0.12),
        [WidgetCatalog.FuelCalculator] = (0.80, 0.40),
        [WidgetCatalog.Cockpit] = (0.40, 0.74),
        [WidgetCatalog.PedalTrace] = (0.62, 0.80),
        [WidgetCatalog.TireInfo] = (0.84, 0.70),
    };

    /// <summary>Where a widget starts on a monitor, in desktop WPF units.</summary>
    public static Point PositionOn(string widget, Rect monitor)
    {
        var (x, y) = Spots.GetValueOrDefault(widget, (0.1, 0.1));
        return new Point(Math.Round(monitor.Left + (x * monitor.Width)), Math.Round(monitor.Top + (y * monitor.Height)));
    }
}
