using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Drag-to-reorder for a <see cref="ReorderListSetting"/>'s list, by plain mouse capture rather than
/// WPF drag-and-drop (the same choice the layout editor made): press on a row's handle, move, and
/// the row trades places with each neighbour it passes; release applies the order once.
/// Set <c>IsEnabled</c> on the ItemsControl and <c>IsHandle</c> on the grip inside each row.
/// </summary>
public static class ReorderListDrag
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ReorderListDrag), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IsHandleProperty = DependencyProperty.RegisterAttached(
        "IsHandle", typeof(bool), typeof(ReorderListDrag), new PropertyMetadata(false));

    // The row being dragged, per list; only one drag can be under way at a time.
    private static readonly DependencyProperty DraggedProperty = DependencyProperty.RegisterAttached(
        "Dragged", typeof(ReorderListItem), typeof(ReorderListDrag), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsHandle(DependencyObject element) => (bool)element.GetValue(IsHandleProperty);

    public static void SetIsHandle(DependencyObject element, bool value) => element.SetValue(IsHandleProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list || e.NewValue is not true)
        {
            return;
        }

        list.PreviewMouseLeftButtonDown += OnPress;
        list.PreviewMouseMove += OnMove;
        list.PreviewMouseLeftButtonUp += OnRelease;
        list.LostMouseCapture += OnLostCapture;
    }

    private static void OnPress(object sender, MouseButtonEventArgs e)
    {
        var list = (ItemsControl)sender;
        if (!OnHandle(e.OriginalSource as DependencyObject, list) ||
            (e.OriginalSource as FrameworkElement)?.DataContext is not ReorderListItem item)
        {
            return;
        }

        list.SetValue(DraggedProperty, item);
        item.IsDragging = true;
        list.CaptureMouse();
        e.Handled = true;
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        var list = (ItemsControl)sender;
        if (list.GetValue(DraggedProperty) is not ReorderListItem item || list.DataContext is not ReorderListSetting setting)
        {
            return;
        }

        var from = setting.Items.IndexOf(item);
        var to = IndexUnder(list, e.GetPosition(list).Y);
        if (to >= 0 && to != from)
        {
            setting.MoveLive(from, to);
        }
    }

    private static void OnRelease(object sender, MouseButtonEventArgs e)
    {
        var list = (ItemsControl)sender;
        if (list.GetValue(DraggedProperty) is not null)
        {
            // Ends the drag through LostMouseCapture, which also covers a capture lost any other way.
            list.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        var list = (ItemsControl)sender;
        if (list.GetValue(DraggedProperty) is not ReorderListItem item)
        {
            return;
        }

        list.ClearValue(DraggedProperty);
        item.IsDragging = false;
        (list.DataContext as ReorderListSetting)?.CommitOrder();
    }

    /// <summary>The row whose box the pointer is in; above the first or below the last, that end.</summary>
    private static int IndexUnder(ItemsControl list, double y)
    {
        var count = list.Items.Count;
        for (var i = 0; i < count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row)
            {
                continue;
            }

            var top = row.TranslatePoint(new Point(0, 0), list).Y;
            if (y < top + row.ActualHeight)
            {
                return i;
            }
        }

        return count - 1;
    }

    private static bool OnHandle(DependencyObject? element, DependencyObject stop)
    {
        for (var node = element; node is not null && node != stop; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (GetIsHandle(node))
            {
                return true;
            }
        }

        return false;
    }
}
