using System.Windows;
using System.Windows.Input;
using IRacingOverlay.App.About;
using IRacingOverlay.App.Diagnostics;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>The one-time notice after an update. Only reports what was chosen; the owner decides
/// what "View details" opens.</summary>
public partial class UpdateNoticeWindow : Window
{
    public UpdateNoticeWindow(UpdateNotice notice)
    {
        InitializeComponent();
        DataContext = notice;
    }

    public event Action? DetailsRequested;

    private void OnViewDetails(object sender, RoutedEventArgs e)
    {
        AppLog.Activity("Updates", "Update notice: view details");
        Close();
        DetailsRequested?.Invoke();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        AppLog.Activity("Updates", "Update notice dismissed");
        Close();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
