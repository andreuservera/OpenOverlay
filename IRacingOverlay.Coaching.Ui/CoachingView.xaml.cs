using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>
/// The Coaching page. Hosted by the app's control panel; it brings its own view model, so the host
/// only has to place it and show or hide it. The saved sessions are read each time the page comes
/// into view, so one coached in the meantime shows up without a restart.
/// </summary>
public partial class CoachingView : UserControl
{
    // One model for the window's lifetime: the host creates the view each time the page opens, and
    // the session and corner picked should still be there when the driver comes back to it.
    private static CoachingViewModel? _shared;
    private readonly CoachingViewModel _model = _shared ??= new CoachingViewModel();

    public CoachingView()
    {
        InitializeComponent();
        DataContext = _model;
        // Left edge on the tag's, just under it, whatever side Windows would open popups on.
        BetaTip.CustomPopupPlacementCallback = (_, target, _) =>
            [new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(0, target.Height + 4), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)];
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                _model.OnShown();
            }
        };
    }

    // A list inside the page's scroller would swallow the wheel; hand it to the page instead.
    private void List_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject source)
        {
            return;
        }

        e.Handled = true;
        var parent = VisualTreeHelper.GetParent(source) as UIElement;
        parent?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender });
    }
}
