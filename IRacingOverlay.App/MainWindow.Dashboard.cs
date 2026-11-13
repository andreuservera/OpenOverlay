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

/// <summary>The fullscreen dashboard window and recovering the overlays as a whole.</summary>
public partial class MainWindow
{
    private void ToggleDashboard()
    {
        if (!ControlPanelViewModel.DashboardAvailable && _dashboard is not { IsVisible: true })
        {
            return;
        }

        if (_dashboard is { IsVisible: true })
        {
            AppLog.Activity("Dashboard", "Hidden");
            _dashboard.Hide();
            SetDashboardButtonCaption(Loc.T("layouts.showDashboard"));
            return;
        }

        var screens = Screen.AllScreens;
        if (screens.Length == 0)
        {
            AppLog.Warn("Dashboard", "No display found to show the dashboard on");
            return;
        }

        var display = Math.Clamp(_vm.SelectedMonitorIndex, 0, screens.Length - 1);
        AppLog.Activity("Dashboard", $"Shown on display {display + 1}");
        _dashboard ??= CreateDashboard();
        _dashboard.MoveToScreen(screens[display]);
        SetDashboardButtonCaption(Loc.T("layouts.hideDashboard"));
    }

    private DashboardWindow CreateDashboard()
    {
        var dashboard = new DashboardWindow();
        dashboard.SetFlagOptions(_vm.FlagOptions);
        dashboard.SetCockpitOptions(_vm.CockpitOptions);
        dashboard.SetFuelCalculatorOptions(_vm.FuelCalculatorOptions);
        dashboard.Closed += (_, _) =>
        {
            // Closed with Alt+F4: a closed window can't be shown again, so the next toggle builds a new one.
            if (ReferenceEquals(_dashboard, dashboard))
            {
                AppLog.Activity("Dashboard", "Closed");
                _dashboard = null;
            }

            SetDashboardButtonCaption(Loc.T("layouts.showDashboard"));
        };
        return dashboard;
    }

    private void SetDashboardButtonCaption(string caption)
    {
        if (_vm.DashboardButton is { } button)
        {
            button.ButtonText = caption;
        }
    }

    /// <summary>Replaces the dashboard window, reopening it where it was if it was on screen.</summary>
    private void RecoverDashboard()
    {
        if (_dashboard is not { } broken)
        {
            return;
        }

        var wasVisible = broken.IsVisible;
        _dashboard = null;
        try
        {
            broken.Close();
        }
        catch (Exception e) when (!ExceptionPolicy.IsFatal(e))
        {
            AppLog.Warn("Dashboard", "Could not close the failed dashboard window", e);
        }

        if (wasVisible)
        {
            ToggleDashboard();
        }
    }

    /// <summary>Last resort for a burst of unhandled UI exceptions: fresh windows for everything.</summary>
    private void RecoverOverlays()
    {
        _vm.RestartOverlays();
        RecoverDashboard();
    }
}
