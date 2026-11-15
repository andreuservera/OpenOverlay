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

/// <summary>The tray icon, hiding to it and coming back, and leaving the app.</summary>
public partial class MainWindow
{
    private void ToggleControlPanel()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            HideToTray();
            return;
        }

        RestoreFromTray();
    }

    private void HideToTray()
    {
        AppLog.Activity("Control Panel", "Hidden to the tray");
        Hide();
    }

    /// <summary>Back to the state it was in before it went away, and in front of whatever has focus.</summary>
    internal void RestoreFromTray()
    {
        AppLog.Activity("Control Panel", "Opened");
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = _restoreState;
        }

        // Windows refuses to hand focus to a background app outright; a topmost flip gets it in front.
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        ShowUpdateNoticeIfPending();
    }

    /// <summary>
    /// The one-time notice after an update, over the control panel. It waits for the panel to be on
    /// screen — from the tray, the next time it's opened — and is marked shown before it appears,
    /// so not even a crash brings it back a second time.
    /// </summary>
    private void ShowUpdateNoticeIfPending()
    {
        if (!InstallHistory.NoticePending || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        InstallHistory.MarkNoticeShown();
        try
        {
            var notice = new UpdateNoticeWindow(UpdateNotice.For(BuildInfo.Version, ReleaseCatalog.Current))
            {
                Owner = this,
                ShowActivated = IsActive,
            };
            notice.DetailsRequested += () =>
            {
                RestoreFromTray();
                _vm.ShowWhatsNew();
            };
            notice.Show();
            AppLog.Info("Updates", "Update notice shown", new Dictionary<string, string> { ["version"] = BuildInfo.Version });
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            AppLog.Warn("Updates", "Update notice could not be shown", e);
        }
    }

    private void ExitApplication()
    {
        // The layout editor may hold unsaved changes; the user can still back out of exiting.
        if (!_vm.CloseLayoutEditor())
        {
            return;
        }

        AppLog.Activity("Control Panel", "Exit requested");
        _exiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting || TrayPreferencesStore.CloseBehavior == CloseBehavior.Exit)
        {
            // A first close with "Exit application" set: the layout editor gets its say before the
            // app goes. ExitApplication has already asked, and session end must not wait.
            if (!_exiting && !_vm.CloseLayoutEditor())
            {
                e.Cancel = true;
                return;
            }

            if (!_exiting)
            {
                AppLog.Activity("Control Panel", "Closed; exiting");
            }

            _exiting = true;
            ShutdownGuard.Step("closing the control panel");
            ShutdownGuard.Arm(TimeSpan.FromSeconds(10));
            return;
        }

        e.Cancel = true;
        HideToTray();
        if (!TrayPreferencesStore.TrayNoticeShown)
        {
            _tray?.ShowNotice(
                Loc.T("tray.stillRunning"),
                Loc.T("tray.stillRunning.body"));
            TrayPreferencesStore.MarkTrayNoticeShown();
        }
    }

    private void UpdateTray()
    {
        if (_tray is null)
        {
            return;
        }

        var (status, text) = _vm.Health == HealthStatus.Failed
            ? (TrayStatus.Error, Loc.F("tray.status.problem", _vm.HealthLine))
            : _connection.LastError is { } error
            ? (TrayStatus.Error, Loc.F("tray.status.telemetryError", error))
            : !_connection.IsConnected
                ? (TrayStatus.NoSession, Loc.T("connection.waiting"))
                : _vm.OverlaysHidden
                    ? (TrayStatus.OverlaysHidden, Loc.T("tray.status.hidden"))
                    : (TrayStatus.Running, Loc.T("tray.status.on"));
        _tray.Update(status, $"OpenOverlay — {text}", _vm.OverlaysHidden);
    }
}
