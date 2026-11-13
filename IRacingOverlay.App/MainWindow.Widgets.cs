using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IRacingOverlay.App.About;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Dashboard;
using IRacingOverlay.App.Diagnostics;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.TrackMaps;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;
using IRacingOverlay.Sdk;
using Screen = System.Windows.Forms.Screen;

namespace IRacingOverlay.App;

/// <summary>Feeding the widgets their state, clearing them when iRacing goes away, and rebuilding one that failed.</summary>
public partial class MainWindow
{
    /// <summary>
    /// Puts every widget and the dashboard back to its no-data state when iRacing goes away. The
    /// update loop stops pushing once there's no telemetry, so without this the last session's
    /// numbers would stay on screen looking live. The tables show "Waiting for iRacing telemetry…".
    /// </summary>
    private void ClearWidgets()
    {
        _latestStandings = [];
        _lastCockpitFeed = null;
        _vm.TelemetryLine = Loc.T("connection.waiting");

        _sessionType = "";
        _strengthOfField = 0;
        Clear(WidgetCatalog.Relative, Relative, w =>
        {
            w.UpdateRows([]);
            w.SetSof(0);
            w.SetProgress(SessionProgress.Empty);
            w.SetConditions(TableConditions.Empty);
        });
        Clear(WidgetCatalog.Standings, Standings, w =>
        {
            w.UpdateRows([]);
            w.SetSof(0);
            w.SetProgress(SessionProgress.Empty);
            w.SetConditions(TableConditions.Empty);
        });
        Clear(WidgetCatalog.Cockpit, Cockpit, w => w.UpdateState(CockpitState.Empty));
        Clear(WidgetCatalog.Flag, Flags, w => w.UpdateState([FlagState.None]));
        Clear(WidgetCatalog.TireInfo, Tires, w => w.UpdateState(TireInfoState.Empty));
        Clear(WidgetCatalog.Delta, Delta, w => w.UpdateState(DeltaState.Empty));
        Clear(WidgetCatalog.PedalTrace, Pedals, w => w.UpdateState(PedalTraceState.Empty));
        Clear(WidgetCatalog.Incident, Incidents, w => w.UpdateState(IncidentState.Empty));
        Clear(WidgetCatalog.TrackInfo, TrackInfo, w => w.UpdateState(TrackInfoState.Empty));
        Clear(WidgetCatalog.Weather, Weather, w => w.UpdateState(WeatherState.Empty));
        Clear(WidgetCatalog.TrackMap, TrackMap, w => w.UpdateState([]));
        Clear(WidgetCatalog.CircuitMap, CircuitMap, w => w.UpdateState(CircuitMapState.Empty));
        Clear(WidgetCatalog.Radar, Radar, w => w.UpdateState(RadarState.Empty));
        Clear(WidgetCatalog.FuelCalculator, FuelCalculator, w => w.UpdateState(FuelCalculatorState.Empty));

        if (_dashboard is { } dashboard)
        {
            _dashboardGuard.Run(() =>
            {
                dashboard.UpdateStandingsRows([]);
                dashboard.UpdateStandingsSof(0);
                dashboard.UpdateRelativeRows([]);
                dashboard.UpdateSessionProgress(SessionProgress.Empty);
                dashboard.UpdateCockpit(CockpitState.Empty);
                dashboard.UpdateFlag([FlagState.None]);
                dashboard.UpdateTireInfo(TireInfoState.Empty);
                dashboard.UpdateDelta(DeltaState.Empty);
                dashboard.UpdateFuelCalculator(FuelCalculatorState.Empty);
                dashboard.UpdatePedalTrace(PedalTraceState.Empty);
                dashboard.UpdateIncident(IncidentState.Empty);
                dashboard.UpdateTrackInfo(TrackInfoState.Empty);
                dashboard.UpdateTrackMap([]);
            });
        }
    }

    private void Clear<TWidget>(string key, TWidget? widget, Action<TWidget> clear) where TWidget : OverlayWindowBase
    {
        if (widget is not null)
        {
            _widgetGuards[key].Run(() => clear(widget));
        }
    }

    /// <summary>
    /// Builds one widget's state and hands it to the floating widget and the dashboard, each step
    /// under a guard: a builder that throws leaves the windows showing their previous state, a window
    /// that throws doesn't stop the dashboard getting the same state. Nothing is built for nobody.
    /// </summary>
    private void Feed<TWidget, TState>(
        string key,
        TWidget? widget,
        Func<TState> build,
        Action<TWidget, TState> toWidget,
        Action<DashboardWindow, TState>? toDashboard)
        where TWidget : OverlayWindowBase
    {
        var dashboard = toDashboard is null ? null : _dashboard;
        if (widget is null && dashboard is null)
        {
            return;
        }

        if (!_perfNames.TryGetValue(key, out var names))
        {
            names = ($"{key}.build", $"{key}.apply", $"{key}.dashboard");
            _perfNames[key] = names;
        }

        var guard = _widgetGuards[key];
        var mark = PerfProbe.Begin();
        if (!guard.TryRun(build, out var state))
        {
            return;
        }

        PerfProbe.End(names.Build, mark);

        if (widget is not null)
        {
            mark = PerfProbe.Begin();
            guard.Run(() => toWidget(widget, state));
            PerfProbe.End(names.Apply, mark);
        }

        if (dashboard is not null)
        {
            mark = PerfProbe.Begin();
            _dashboardGuard.Run(() => toDashboard!(dashboard, state));
            PerfProbe.End(names.Dashboard, mark);
        }
    }

    /// <summary>The driver tables' header fields are pushed on the standings tick, so a header
    /// switched off has to be cleared now rather than leaving a stale value on screen for up to a
    /// second.</summary>
    private void PushTableHeader(DriverTable table)
    {
        if (table == DriverTable.Standings)
        {
            Clear(WidgetCatalog.Standings, Standings, w =>
            {
                w.SetSessionType(_sessionType);
                w.SetSof(_strengthOfField);
            });
        }
        else
        {
            Clear(WidgetCatalog.Relative, Relative, w =>
            {
                w.SetSessionType(_sessionType);
                w.SetSof(_strengthOfField);
            });
        }
    }

    /// <summary>A widget's guard tripped. Its state is rebuilt from scratch either way; the window
    /// is only replaced when drawing into it was what failed.</summary>
    private void RecoverWidget(string key, GuardStage stage)
    {
        switch (key)
        {
            case WidgetCatalog.FuelCalculator:
                _fuelCalculatorBuilder = new(_lapLog);
                break;
            case WidgetCatalog.PedalTrace:
                _pedalTraceBuilder = new();
                break;
            case WidgetCatalog.Flag:
                _flagPresenter = new();
                break;
        }

        if (stage == GuardStage.Render)
        {
            _vm.SlotOf(key).Restart();
        }
    }

    /// <summary>On the telemetry thread. A failure here must never reach the reader.</summary>
    private void ObserveTrackOutline(TelemetrySnapshot snapshot)
    {
        try
        {
            _trackOutlines.Observe(snapshot, _connection.Session);
        }
        catch (Exception e)
        {
            AppLog.Warn("TrackMap", "Track shape learning failed", e);
        }
    }
}
