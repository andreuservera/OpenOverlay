using System.Windows;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>The options objects a previewed panel binds to. Null entries fall back to defaults, as
/// before any options have been handed over.</summary>
public sealed record PreviewOptions(
    DriverTableOptions? Standings = null,
    DriverTableOptions? Relative = null,
    FuelCalculatorOptions? FuelCalculator = null,
    FlagOptions? Flag = null,
    FlagPreviewScenario? FlagPreview = null,
    CockpitOptions? Cockpit = null,
    WeatherOptions? Weather = null);

/// <summary>
/// Builds a widget's production panel and feeds it sample data through the same UpdateState/SetRows
/// calls the telemetry loop uses. Shared by the control panel's preview and the layout editor's
/// canvas, so both draw the real widget rather than a drawing of it.
/// </summary>
public static class PanelFactory
{
    public static UIElement? Create(string key, PreviewOptions options) => key switch
    {
        WidgetCatalog.Relative => new RelativePanel { Options = options.Relative ?? new DriverTableOptions(DriverTable.Relative) },
        WidgetCatalog.Standings => new StandingsPanel { Options = options.Standings ?? new DriverTableOptions(DriverTable.Standings) },
        WidgetCatalog.Cockpit => new CockpitPanel { Options = options.Cockpit ?? new CockpitOptions() },
        WidgetCatalog.Flag => new FlagPanel { Options = options.Flag ?? new FlagOptions() },
        WidgetCatalog.TireInfo => new TireInfoPanel(),
        WidgetCatalog.Delta => new DeltaPanel(),
        WidgetCatalog.PedalTrace => new PedalTracePanel(),
        WidgetCatalog.Incident => new IncidentPanel(),
        WidgetCatalog.TrackInfo => new TrackInfoPanel(),
        WidgetCatalog.Weather => new WeatherPanel { Options = options.Weather ?? new WeatherOptions() },
        WidgetCatalog.TrackMap => new TrackMapPanel(),
        WidgetCatalog.FuelCalculator => new FuelCalculatorPanel { Options = options.FuelCalculator ?? new FuelCalculatorOptions() },
        _ => null,
    };

    /// <summary>Pushes the sample state into a panel built by <see cref="Create"/>. Cheap enough to
    /// call on every option change: one pass over at most a couple of dozen rows.</summary>
    public static void PushMockState(UIElement? panel, PreviewOptions options)
    {
        switch (panel)
        {
            case StandingsPanel standings:
                {
                    var tableOptions = options.Standings;
                    var field = PreviewData.StandingsField(tableOptions?.ShowMulticlass == true);
                    var focusSize = tableOptions?.FocusSize ?? DriverTableOptions.DefaultStandingsFocusSize;
                    standings.SetRows(tableOptions?.ShowMulticlass == true
                        ? StandingsBuilder.BuildMulticlassView(field, focusSize)
                        : StandingsBuilder.BuildFocusedView(field, focusSize));
                    standings.SetSof(PreviewData.StrengthOfField());
                    standings.SetClassName(PreviewData.ClassName);
                    standings.SetSessionId(PreviewData.SubSessionId);
                    standings.SetProgress(PreviewData.Progress());
                    break;
                }

            case RelativePanel relative:
                {
                    var focusSize = options.Relative?.FocusSize ?? DriverTableOptions.DefaultRelativeFocusSize;
                    relative.SetRows(PreviewData.RelativeRows(focusSize));
                    relative.SetClassName(PreviewData.ClassName);
                    relative.SetSessionId(PreviewData.SubSessionId);
                    relative.SetProgress(PreviewData.Progress());
                    break;
                }

            case CockpitPanel cockpit:
                cockpit.UpdateState(PreviewData.Cockpit());
                break;
            case FlagPanel flag:
                {
                    // Same selection the live widget makes; a disabled flag shows the placeholder,
                    // which is the honest answer to "what will I see".
                    var flags = FlagPresenter.Compose(
                        options.FlagPreview?.Flags ?? PreviewData.FlagScenarios[0].Flags,
                        options.Flag ?? new FlagOptions());
                    flag.UpdateState(flags.Count > 0 ? flags : [FlagState.None]);
                    break;
                }
            case TireInfoPanel tires:
                tires.UpdateState(PreviewData.Tires());
                break;
            case DeltaPanel delta:
                delta.UpdateState(PreviewData.Delta());
                break;
            case PedalTracePanel pedals:
                pedals.UpdateState(PreviewData.PedalTrace());
                break;
            case IncidentPanel incidents:
                incidents.UpdateState(PreviewData.Incidents());
                break;
            case TrackInfoPanel trackInfo:
                trackInfo.UpdateState(PreviewData.TrackInfo());
                break;
            case WeatherPanel weather:
                weather.UpdateState(PreviewData.Weather());
                break;
            case TrackMapPanel trackMap:
                trackMap.UpdateState(PreviewData.TrackMap());
                break;
            case FuelCalculatorPanel fuelCalculator:
                fuelCalculator.UpdateState(PreviewData.FuelCalculator());
                break;
        }
    }
}
