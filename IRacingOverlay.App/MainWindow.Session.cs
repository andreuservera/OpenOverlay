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

/// <summary>Which event is being driven, the connection's faults and the health report.</summary>
public partial class MainWindow
{
    private void ResetLapHistory()
    {
        _lapLog = new();
        _fuelCalculatorBuilder = new(_lapLog);
    }

    /// <summary>
    /// Keys the per-session trackers (fuel history, pit stops, best laps, flag timers) to the event
    /// they were built from. A different event resets them, so a restarted sim never shows another
    /// car's fuel burn; the same event keeps them, so rejoining after an iRacing crash carries on.
    /// </summary>
    /// <summary>The car the player is in, as iRacing names it; null before the session is known.</summary>
    private static string? PlayerCar(IracingSessionInfo? session) =>
        session?.DriverInfo is { } info && info.Drivers.FirstOrDefault(d => d.CarIdx == info.DriverCarIdx) is { CarScreenName: { Length: > 0 } name }
            ? name
            : null;

    private void ObserveSession(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (ReferenceEquals(session, _observedSession) || session?.WeekendInfo is not { } weekend)
        {
            return;
        }

        _observedSession = session;
        var driverInfo = session.DriverInfo;
        var playerCar = driverInfo?.Drivers.FirstOrDefault(d => d.CarIdx == driverInfo.DriverCarIdx);
        var sessionType = _statusGuard.TryRun(() => CurrentSession.Entry(telemetry, session)?.SessionType, out var type) ? type : null;
        _sessionDescription = string.Join(" · ", new[]
        {
            weekend.TrackDisplayName ?? weekend.TrackName,
            string.IsNullOrEmpty(sessionType) ? null : sessionType,
            weekend.SubSessionID > 0 ? $"subsession {weekend.SubSessionID.ToString(CultureInfo.InvariantCulture)}" : "offline",
            playerCar?.CarScreenNameShort,
        }.Where(part => !string.IsNullOrEmpty(part)));
        AppLog.Session = _sessionDescription;
        _sessionLabel = string.Join(" · ", new[]
        {
            weekend.TrackDisplayShortName ?? weekend.TrackDisplayName ?? weekend.TrackName,
            string.IsNullOrEmpty(sessionType) ? null : sessionType,
        }.Where(part => !string.IsNullOrEmpty(part)));

        var key = string.Create(CultureInfo.InvariantCulture, $"{weekend.SubSessionID}|{weekend.TrackName}|{playerCar?.CarID}");
        if (key == _sessionKey)
        {
            if (_awaitingReconnect)
            {
                _awaitingReconnect = false;
                AppLog.Info("Session", "Rejoined the same event; session state kept");
            }

            return;
        }

        var isNewEvent = _sessionKey is not null;
        _sessionKey = key;
        _awaitingReconnect = false;
        if (isNewEvent)
        {
            ResetLapHistory();
            _pedalTraceBuilder = new();
            _sessionBestLapTracker = new();
            _pitStopTracker = new();
            _lineCrossings = new();
            _estTimeProfile = new();
            _penaltyTracker = new();
            _flagPresenter = new();
            _lastPreGreenGrid = null;
            _latestStandings = [];
        }

        AppLog.Info("Session", isNewEvent ? "New event; per-session state reset" : "Session detected", new Dictionary<string, string>
        {
            ["perCarFlags"] = telemetry.HasVariable(TelemetryVarNames.CarIdxSessionFlags).ToString(),
        });
    }

    private void OnConnectionFault(object? sender, ConnectionFault fault)
    {
        // Raised on the reader thread; the logger is thread-safe and nothing else is touched here.
        var (level, message) = fault.Stage switch
        {
            "read" => (fault.ConsecutiveFailures >= 5 ? LogLevel.Error : LogLevel.Warning, "Telemetry read failed; retrying"),
            "watchdog" => (LogLevel.Warning, "No new telemetry; reopening shared memory"),
            "supervisor" => (LogLevel.Error, "Telemetry reader restarted"),
            _ when fault.Stage.StartsWith("subscriber", StringComparison.Ordinal) => (LogLevel.Error, "Telemetry event handler failed"),
            _ when fault.Stage.Contains("repaired", StringComparison.Ordinal) => (LogLevel.Info, "Session info repaired before parsing"),
            _ => (LogLevel.Warning, "Session info could not be fully parsed; keeping the last good values"),
        };

        var data = new Dictionary<string, string>
        {
            ["stage"] = fault.Stage,
            ["consecutive"] = fault.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture),
        };
        if (fault.RetryIn is { } retry)
        {
            data["retryInSeconds"] = retry.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        }

        if (AppLog.Current.Write(level, "Telemetry", message, fault.Exception, data)?.Ref is { } reference)
        {
            _telemetryErrorRef = reference;
        }
    }

    /// <summary>Once a second: roll every component into the health report, show it, and publish
    /// the snapshot a crash report would need.</summary>
    private void UpdateHealth()
    {
        var now = DateTime.UtcNow;
        var telemetry = _connection.Health;
        var sinceTick = telemetry.LastTickUtc is { } lastTick ? (now - lastTick).TotalSeconds : 0;
        var (status, summary) = telemetry.State switch
        {
            ConnectionState.Connected when telemetry.SessionInfoDegraded =>
                (HealthStatus.Degraded, "connected; session info needed repair"),
            ConnectionState.Connected => (HealthStatus.Healthy, "connected"),
            ConnectionState.Stale => (HealthStatus.Degraded, $"no new data for {sinceTick:0} s"),
            ConnectionState.Recovering when telemetry.ConsecutiveFailures >= 5 =>
                (HealthStatus.Failed, $"reconnecting after {telemetry.ConsecutiveFailures} errors"),
            ConnectionState.Recovering => (HealthStatus.Degraded, "reconnecting"),
            _ => (HealthStatus.Healthy, "waiting for iRacing"),
        };
        _health.Report("Telemetry", status, summary, telemetry.LastTickUtc, telemetry.LastErrorUtc, telemetry.TotalFailures, telemetry.LastError, _telemetryErrorRef);

        _health.Report("UI thread", _watchdog.Status, _watchdog.Status == HealthStatus.Healthy
            ? "responsive"
            : $"stalled {_watchdog.Stall.TotalSeconds:0} s");

        var settingsProblem = SettingsFile.LastWriteFailureUtc is { } settingsFailure && now - settingsFailure < RecentProblemWindow;
        _health.Report(
            "Settings",
            settingsProblem ? HealthStatus.Degraded : HealthStatus.Healthy,
            settingsProblem ? "a change could not be saved" : "saved",
            lastFailureUtc: SettingsFile.LastWriteFailureUtc,
            failures: SettingsFile.WriteFailures,
            lastError: SettingsFile.LastWriteError,
            lastErrorRef: SettingsFile.LastWriteErrorRef);

        // Updates never affect the overlay itself, so a failed check is only ever a degradation.
        _health.Report(
            "Updates",
            _updates.State == UpdateState.Failed ? HealthStatus.Degraded : HealthStatus.Healthy,
            _updates.Detail,
            failures: _updates.Failures,
            lastErrorRef: _updates.LastErrorRef);
        _vm.UpdateStatus = _updates.DisplayDetail;

        var uiProblem = GlobalExceptionHandler.LastHandledUtc is { } uiFailure && now - uiFailure < RecentProblemWindow;
        _health.Report(
            "Unhandled UI errors",
            uiProblem ? HealthStatus.Degraded : HealthStatus.Healthy,
            uiProblem ? $"{GlobalExceptionHandler.HandledCount} contained so far" : "none recently",
            lastFailureUtc: GlobalExceptionHandler.LastHandledUtc,
            failures: GlobalExceptionHandler.HandledCount,
            lastErrorRef: GlobalExceptionHandler.LastRef);

        var report = _health.Snapshot();
        _vm.Health = report.Overall;
        // The healthy line is the one people see every day; a problem keeps its English detail,
        // which is what a bug report needs.
        _vm.HealthLine = report.Overall == HealthStatus.Healthy ? Loc.T("health.allHealthy") : report.Summary;
        _vm.ConnectionState = telemetry.State;
        _vm.SessionLabel = _connection.IsConnected ? _sessionLabel : null;

        DiagnosticsReport.LatestContext = new DiagnosticsContext(
            _connection.IsConnected ? _sessionDescription : null,
            telemetry,
            _vm.Slots.Select(DescribeSlot).Append($"Dashboard: {(_dashboard is { IsVisible: true } ? "open" : "closed")}").ToList(),
            _vm.DiagnosticsLine);
    }

    private static string DescribeSlot(WidgetSlot slot) => string.Create(
        CultureInfo.InvariantCulture,
        $"{slot.Descriptor.LogName}: {(slot.IsEnabled ? "on" : "off")}, {slot.StateLogLabel.ToLowerInvariant()}, size {slot.Scale}, opacity {slot.Opacity:P0}{(slot.HideOutsideCar ? ", hides outside car" : "")}");
}
