using System.Text.Json.Nodes;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>What a widget's options are before anyone has touched them: read through the same codec a
/// layout uses, from options objects fresh out of their constructors and saved nowhere.</summary>
public static class WidgetDefaults
{
    public static JsonObject ConfigOf(string type)
    {
        var codecs = WidgetConfigCodecs.Create(new WidgetConfigTargets(
            new DriverTableOptions(DriverTable.Standings),
            new DriverTableOptions(DriverTable.Relative),
            new FlagOptions(),
            new CockpitOptions(),
            new WeatherOptions(),
            new TrackInfoOptions(),
            new FuelCalculatorOptions(),
            new DeltaOptions(),
            new PedalTraceOptions(),
            new WidgetConfigPersistence(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }),
            _ => { }));
        return codecs[type].Read();
    }
}
