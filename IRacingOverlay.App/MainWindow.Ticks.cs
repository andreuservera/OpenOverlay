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

/// <summary>The two update loops: the general 100 ms tick and the fast tick for the cockpit, radar and pedals, with their timing diagnostics.</summary>
public partial class MainWindow
{
    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        _watchdog.Beat();
        _uiTickStopwatch.Restart();
        var perfMark = PerfProbe.Begin();
        try
        {
            UiTimer_TickCore();
        }
        finally
        {
            PerfProbe.End("ui.tick", perfMark);
            if (PerfProbe.Enabled)
            {
                // Whatever the tick queued (layout, render, input) runs before ContextIdle: the gap
                // to this callback is the WPF work the tick itself doesn't see.
                _afterTickMark = PerfProbe.Begin();
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => PerfProbe.End("ui.after-tick (layout+render)", _afterTickMark));
                PerfProbe.MaybeFlush();
            }

            RecordTickDuration(_uiTickStopwatch.Elapsed.TotalMilliseconds, ref _uiTickTotalMs, ref _uiTickMaxMs, ref _uiTickSamples);
            if (++_healthTicks % HealthUpdateEveryNTicks == 0)
            {
                _healthGuard.Run(UpdateHealth);
            }

            _trayGuard.Run(UpdateTray);
        }
    }

    private void UiTimer_TickCore()
    {
        var telemetry = _connection.Latest;

        // Evaluated before the no-telemetry bail-out, and from the connection as well as the
        // snapshot. Both matter: this used to sit after the bail-out, so with iRacing closed the
        // loop returned early and every widget stayed on screen forever — and IRacingConnection
        // keeps the last snapshot it read after a disconnect, so the stale IsOnTrack in it would
        // have answered "still driving" even once the sim was gone.
        // A failure here fails open (driving), for the same reason a missing IsOnTrack does below.
        var isDriving = !_autoHideGuard.TryRun(() => IsPlayerDriving(telemetry), out var driving) || driving;
        ApplyAutoHideVisibility(isDriving);

        if (telemetry is null)
        {
            return;
        }

        var session = _connection.Session;
        ObserveSession(telemetry, session);
        _statusGuard.Run(() => _vm.SetDrivingContext(
            Layouts.LayoutAutoOpen.KindOf(CurrentSession.Entry(telemetry, session)?.SessionType), PlayerCar(session)));
        _statusGuard.Run(() =>
        {
            Units.Observe(telemetry);
            UpdateTelemetryLine(telemetry);
        }, GuardStage.Build);

        _tickCount++;
        var trackersMark = PerfProbe.Begin();
        // Every tick, whatever is open: a stop is timed on entry and exit, and a missed edge loses it.
        _pitStopGuard.Run(() => _pitStopTracker.Update(telemetry, session), GuardStage.Build);
        // Every tick too: a crossing is timed from the ticks either side of the line, and the time
        // curve is learned from wherever the cars are each tick.
        _lineCrossingGuard.Run(() => _lineCrossings.Update(telemetry, session), GuardStage.Build);
        _estTimeProfileGuard.Run(() => _estTimeProfile.Update(telemetry, session), GuardStage.Build);
        _lapLogGuard.Run(() => _lapLog.Observe(telemetry), GuardStage.Build);
        _penaltyGuard.Run(() =>
        {
            foreach (var (driver, penalties) in _penaltyTracker.Update(telemetry, session))
            {
                AppLog.Activity("Flags", "Penalty flags changed", new Dictionary<string, string>
                {
                    ["car"] = driver.CarNumber,
                    ["carIdx"] = driver.CarIdx.ToString(CultureInfo.InvariantCulture),
                    ["player"] = (driver.CarIdx == session?.DriverInfo?.DriverCarIdx).ToString(),
                    ["black"] = penalties.Black.ToString(),
                    ["furled"] = penalties.Furled.ToString(),
                    ["meatball"] = penalties.Meatball.ToString(),
                });
            }
        }, GuardStage.Build);
        PerfProbe.End("trackers", trackersMark);
        // Relative needs the standings order too, for its POS and iRΔ columns, so this runs
        // whenever any of the three consumers is open — not just the two that display it directly.
        // Every tick: the classification itself only moves as cars cross the line, but pit road,
        // flags and tyres are live and should show the moment they change.
        var needsStandings = Standings is not null || _dashboard is not null || Relative is not null;
        if (needsStandings)
        {
            // A failed rebuild keeps the last good order: a table a tick old beats an empty one.
            var standingsMark = PerfProbe.Begin();
            if (_standingsGuard.TryRun(
                    () => StandingsBuilder.BuildStandings(
                        telemetry, session, _sessionBestLapTracker, _pitStopTracker.LastStops, _lineCrossings,
                        _logPreGreenGrid ??= LogPreGreenGrid),
                    out var standings))
            {
                _latestStandings = standings;
            }

            PerfProbe.End("standings.order", standingsMark);

            // The floating widget gets the compact focused view (podium + a block around the
            // player); the Dashboard has the room for the whole field, grouped by class. Only these
            // two pay for SOF — Relative pulls the running order out of this block but has no use
            // for the field strength.
            Feed(
                WidgetCatalog.Standings,
                Standings,
                () => (
                    Rows: _vm.StandingsOptions.ShowMulticlass
                        ? StandingsBuilder.BuildMulticlassView(_latestStandings, _vm.StandingsOptions.FocusSize)
                        : StandingsBuilder.BuildFocusedView(_latestStandings, _vm.StandingsOptions.FocusSize),
                    Sof: StandingsBuilder.ComputeStrengthOfField(session),
                    SessionType: StandingsBuilder.SessionTypeLabel(telemetry, session)),
                (widget, view) =>
                {
                    widget.UpdateRows(view.Rows);
                    _sessionType = view.SessionType;
                    _strengthOfField = view.Sof;
                    widget.SetSof(view.Sof);
                    widget.SetSessionType(view.SessionType);
                },
                null);

            if (_dashboard is { } dashboard && _dashboardGuard.TryRun(
                    () => (
                        Rows: StandingsBuilder.GroupForDisplay(_latestStandings),
                        Sof: StandingsBuilder.ComputeStrengthOfField(session),
                        SessionType: StandingsBuilder.SessionTypeLabel(telemetry, session)),
                    out var full))
            {
                _dashboardGuard.Run(() =>
                {
                    dashboard.UpdateStandingsRows(full.Rows);
                    dashboard.UpdateStandingsSof(full.Sof);
                    dashboard.UpdateStandingsSessionType(full.SessionType);
                });
            }
        }

        // Built every tick: Relative is about where cars are right now, and a once-a-second refresh
        // is visibly laggy when someone is alongside you.
        Feed(
            WidgetCatalog.Relative,
            Relative,
            () => StandingsBuilder.BuildRelative(
                telemetry, session, _vm.RelativeOptions.FocusSize, _latestStandings, _pitStopTracker.LastStops, _estTimeProfile,
                LogOpeningLapLapped),
            (widget, rows) =>
            {
                widget.UpdateRows(rows);
                _sessionType = StandingsBuilder.SessionTypeLabel(telemetry, session);
                _strengthOfField = StandingsBuilder.ComputeStrengthOfField(session);
                widget.SetSessionType(_sessionType);
                widget.SetSof(_strengthOfField);
            },
            (dashboard, rows) => dashboard.UpdateRelativeRows(rows));

        // The tables' session information: the clock, brake bias, conditions and incidents move every tick, so
        // not on the once-a-second standings beat.
        Feed(
            WidgetCatalog.Standings,
            Standings,
            () => (Progress: SessionProgressBuilder.Build(telemetry, session, _lapLog.RecentLapSeconds()), Conditions: TableConditions.Build(telemetry, session)),
            (widget, info) =>
            {
                widget.SetProgress(info.Progress);
                widget.SetConditions(info.Conditions);
            },
            (dashboard, info) => dashboard.UpdateSessionProgress(info.Progress));
        Feed(
            WidgetCatalog.Relative,
            Relative,
            () => (Progress: SessionProgressBuilder.Build(telemetry, session, _lapLog.RecentLapSeconds()), Conditions: TableConditions.Build(telemetry, session)),
            (widget, info) =>
            {
                widget.SetProgress(info.Progress);
                widget.SetConditions(info.Conditions);
            },
            null);

        Feed(
            WidgetCatalog.Flag,
            Flags,
            () => _flagPresenter.Present(FlagBuilder.Decode(telemetry, session, LogOpeningLapBlue), _vm.FlagOptions, TimeSpan.FromMilliseconds(Environment.TickCount64)),
            (widget, flags) => widget.UpdateState(flags),
            (dashboard, flags) => dashboard.UpdateFlag(flags));

        Feed(WidgetCatalog.TireInfo, Tires, () => TireInfoBuilder.Build(telemetry),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateTireInfo(state));

        Feed(WidgetCatalog.Delta, Delta, () => DeltaBuilder.Build(telemetry, _vm.DeltaReference),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateDelta(state));

        Feed(WidgetCatalog.FuelCalculator, FuelCalculator, () => _fuelCalculatorBuilder.Build(telemetry, session, _vm.FuelCalculatorOptions),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateFuelCalculator(state));

        Feed(WidgetCatalog.Incident, Incidents, () => IncidentBuilder.Build(telemetry, session, _incidentReports.Latest),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateIncident(state));

        Feed(WidgetCatalog.TrackInfo, TrackInfo, () => TrackInfoBuilder.Build(telemetry, session, _lapLog.RecentLapSeconds()),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateTrackInfo(state));

        // Every tick: the wind arrow follows the car's heading, which changes through every corner.
        Feed(WidgetCatalog.Weather, Weather, () => WeatherBuilder.Build(telemetry, session),
            (widget, state) => widget.UpdateState(state), null);

        Feed(WidgetCatalog.TrackMap, TrackMap, () => TrackMapBuilder.Build(telemetry, session),
            (widget, markers) => widget.UpdateState(markers), (dashboard, markers) => dashboard.UpdateTrackMap(markers));

        // The shape is learned on the telemetry thread, and only while the map is on screen.
        _trackOutlines.Wanted = CircuitMap is not null;
        Feed(WidgetCatalog.CircuitMap, CircuitMap,
            () => new CircuitMapState(_trackOutlines.Current, _trackOutlines.LearningProgress, TrackMapBuilder.Build(telemetry, session)),
            (widget, state) => widget.UpdateState(state), null);

        // Memory usage barely changes tick to tick: once a second rather than on every 100ms tick.
        if (_tickCount % DiagnosticsUpdateEveryNTicks == 0)
        {
            _statusGuard.Run(UpdateDiagnostics);
        }
    }

    // Opening-lap blue checks, logged so a live start shows whether they guessed right. Info entries
    // are collapsed as duplicates for a minute, so a check that holds for several ticks is one line.
    private static void LogOpeningLapBlue(OpeningLapBlue check) =>
        AppLog.Info(
            "Flags",
            check.Reason == OpeningLapBlueReason.Unconfirmed ? "Opening-lap blue flag hidden" : "Opening-lap blue flag shown",
            new Dictionary<string, string>
            {
                ["reason"] = check.Reason.ToString(),
                ["lapCompleted"] = check.LapCompleted.ToString(CultureInfo.InvariantCulture),
                ["player"] = check.Player is { } where ? FormatTrackPosition(where) : "-",
                ["byCarIdx"] = check.ByCarIdx?.ToString(CultureInfo.InvariantCulture) ?? "-",
            });

    private static void LogOpeningLapLapped(OpeningLapLapped lapped) =>
        AppLog.Info(
            "Relative",
            lapped.LapCountDoubted ? "Opening-lap lapped cars ignored" : "Opening-lap lapped cars shown",
            new Dictionary<string, string>
            {
                ["lapped"] = lapped.Lapped.ToString(CultureInfo.InvariantCulture),
                ["onTrack"] = lapped.OnTrack.ToString(CultureInfo.InvariantCulture),
                ["player"] = FormatTrackPosition(lapped.Player),
                ["cars"] = string.Join(' ', lapped.Cars.Select(c => $"{c.CarIdx.ToString(CultureInfo.InvariantCulture)}:{FormatTrackPosition(c.Where)}")),
            });

    private void LogPreGreenGrid(PreGreenGrid grid)
    {
        if (grid == _lastPreGreenGrid)
        {
            return;
        }

        _lastPreGreenGrid = grid;
        AppLog.Activity("Standings", "Pre-green grid", new Dictionary<string, string>
        {
            ["gridded"] = grid.Gridded.ToString(CultureInfo.InvariantCulture),
            ["inRoster"] = grid.InRoster.ToString(CultureInfo.InvariantCulture),
            ["missingFromRoster"] = grid.MissingFromRoster.ToString(CultureInfo.InvariantCulture),
            ["inTheWorld"] = grid.InTheWorld.ToString(CultureInfo.InvariantCulture),
        });
    }

    /// <summary>Lap and fraction, e.g. "2+0.713".</summary>
    private static string FormatTrackPosition(TrackPosition where) =>
        string.Create(CultureInfo.InvariantCulture, $"{where.Lap}+{where.LapDistPct:0.000}");

    /// <summary>
    /// Whether the player is actually at the wheel right now — the single question "hide when I'm
    /// not driving" turns on.
    ///
    /// Three conditions, and all three are load-bearing. No live connection means iRacing is closed
    /// or has been exited, and the snapshot still held from before it went away must not be trusted.
    /// No snapshot at all means nothing has been read yet. And IsOnTrack, per iRacing's own SDK
    /// docs, is true "only when the player is running the physics for the car and is currently in
    /// the car" — false at the main menu, in the garage, on a setup screen, while spectating and
    /// during replays, which is the rest of what the option promises.
    ///
    /// A car that simply doesn't publish IsOnTrack falls back to "driving": failing open leaves a
    /// widget visible when it could have hidden, while failing closed would blank someone's overlay
    /// mid-race over a missing variable.
    /// </summary>
    private bool IsPlayerDriving(TelemetrySnapshot? telemetry)
    {
        if (!_connection.IsConnected || telemetry is null)
        {
            return false;
        }

        return !telemetry.HasVariable(TelemetryVarNames.IsOnTrack) || telemetry.GetBool(TelemetryVarNames.IsOnTrack);
    }

    /// <summary>
    /// Hides (or reveals) every widget whose "hide outside car" option is on, without touching the
    /// enabled state itself — so a widget picks back up exactly where it was the moment the player
    /// gets back in the car.
    ///
    /// The decision lives in the slot, not here: showing a widget and then hiding it from a
    /// different code path a tick later is what used to make one flash on screen at startup.
    /// </summary>
    private void ApplyAutoHideVisibility(bool isDriving)
    {
        foreach (var slot in _vm.Slots)
        {
            slot.IsDriving = isDriving;
        }
    }

    /// <summary>Called on the telemetry thread for every new tick. Coalesced: while one update is
    /// still queued on the UI thread, later ticks don't pile up behind it.</summary>
    private void PostCriticalTick()
    {
        if (Interlocked.Exchange(ref _criticalTickPending, 1) == 1)
        {
            return;
        }

        Volatile.Write(ref _criticalPostedTicks, System.Diagnostics.Stopwatch.GetTimestamp());

        Dispatcher.InvokeAsync(OnTelemetryTick, DispatcherPriority.Render);
    }

    private void OnTelemetryTick()
    {
        Volatile.Write(ref _criticalTickPending, 0);
        if (PerfProbe.Enabled)
        {
            // How long the UI thread kept a new telemetry tick waiting: the source of the gaps.
            var waited = System.Diagnostics.Stopwatch.GetElapsedTime(Volatile.Read(ref _criticalPostedTicks));
            PerfProbe.Record("critical.queue-wait", waited.TotalMilliseconds);
        }

        // A few ms of slack, so a 16 ms target runs on every 60 Hz telemetry tick (16.7 ms apart).
        var now = _criticalClock.Elapsed.TotalMilliseconds;
        if (now < _nextCriticalMs - 3)
        {
            return;
        }

        _nextCriticalMs = now + _criticalIntervalMs;
        CriticalTimer_Tick(this, EventArgs.Empty);
    }

    private void CriticalTimer_Tick(object? sender, EventArgs e)
    {
        var nowMs = Environment.TickCount64;
        if (_lastCriticalTickTimestampMs >= 0)
        {
            var gap = nowMs - _lastCriticalTickTimestampMs;
            if (gap > _criticalTickMaxGapMs)
            {
                _criticalTickMaxGapMs = gap;
            }
        }

        _lastCriticalTickTimestampMs = nowMs;

        _criticalTickStopwatch.Restart();
        var perfMark = PerfProbe.Begin();
        try
        {
            CriticalTimer_TickCore();
        }
        finally
        {
            PerfProbe.End("critical.tick", perfMark);
            RecordTickDuration(_criticalTickStopwatch.Elapsed.TotalMilliseconds, ref _criticalTickTotalMs, ref _criticalTickMaxMs, ref _criticalTickSamples);
        }
    }

    private void CriticalTimer_TickCore()
    {
        var telemetry = _connection.Latest;
        if (telemetry is null)
        {
            return;
        }

        var session = _connection.Session;
        var cockpit = Cockpit;
        var cockpitFeed = (telemetry.TickCount, cockpit, _dashboard);
        if (cockpitFeed != _lastCockpitFeed)
        {
            _lastCockpitFeed = cockpitFeed;
            Feed(WidgetCatalog.Cockpit, cockpit, () => CockpitBuilder.Build(telemetry, session),
                (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdateCockpit(state));
        }

        // On the fast tick with the cockpit: a car arriving alongside can't wait for the next 100 ms.
        Feed(WidgetCatalog.Radar, Radar, () => RadarBuilder.Build(telemetry, session),
            (widget, state) => widget.UpdateState(state), null);

        Feed(WidgetCatalog.PedalTrace, Pedals, () => _pedalTraceBuilder.Build(telemetry),
            (widget, state) => widget.UpdateState(state), (dashboard, state) => dashboard.UpdatePedalTrace(state));
    }

    private void UpdateTelemetryLine(TelemetrySnapshot telemetry)
    {
        var units = Units.Read(telemetry);
        var speed = telemetry.HasVariable("Speed") ? Units.SpeedFromMs(telemetry.GetFloat("Speed"), units) : 0;
        var lap = telemetry.HasVariable("Lap") ? telemetry.GetInt("Lap") : 0;
        var gear = telemetry.HasVariable("Gear") ? telemetry.GetInt("Gear") : 0;
        _vm.TelemetryLine = Loc.F("status.telemetry", Math.Round(speed), Units.SpeedUnit(units), lap, gear);
    }

    private void UpdateDiagnostics()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var megabytes = process.WorkingSet64 / (1024.0 * 1024.0);
        var uiAvg = _uiTickSamples > 0 ? _uiTickTotalMs / _uiTickSamples : 0;
        var criticalAvg = _criticalTickSamples > 0 ? _criticalTickTotalMs / _criticalTickSamples : 0;
        var criticalTargetMs = _criticalIntervalMs;

        _vm.DiagnosticsLine =
            $"{megabytes:0} MB · GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} · " +
            $"UI {uiAvg:0.0}/{_uiTickMaxMs:0.0} ms · " +
            $"critical {criticalAvg:0.0}/{_criticalTickMaxMs:0.0} ms (target {criticalTargetMs:0}, worst gap {_criticalTickMaxGapMs:0})";

        // Rolling ~1s window (this is called once every DiagnosticsUpdateEveryNTicks UI ticks) rather
        // than a since-launch average — a stutter from 10 minutes ago shouldn't still be dragging
        // down what the user sees right now.
        _uiTickTotalMs = 0;
        _uiTickMaxMs = 0;
        _uiTickSamples = 0;
        _criticalTickTotalMs = 0;
        _criticalTickMaxMs = 0;
        _criticalTickSamples = 0;
        _criticalTickMaxGapMs = 0;
    }

    private static void RecordTickDuration(double elapsedMs, ref double totalMs, ref double maxMs, ref int samples)
    {
        totalMs += elapsedMs;
        samples++;
        if (elapsedMs > maxMs)
        {
            maxMs = elapsedMs;
        }
    }
}
