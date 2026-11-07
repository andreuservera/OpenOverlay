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
/// <para>The keyboard does the same without the pointer (<c>HandlesKeys</c> on the ItemsControl,
/// <c>IsRow</c> on each focusable row): a click or Tab selects a row, Alt+Up / Alt+Down moves it one
/// place and applies the order, Space shows or hides it.</para>
/// </summary>
public static class ReorderListDrag
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ReorderListDrag), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IsHandleProperty = DependencyProperty.RegisterAttached(
        "IsHandle", typeof(bool), typeof(ReorderListDrag), new PropertyMetadata(false));

    public static readonly DependencyProperty HandlesKeysProperty = DependencyProperty.RegisterAttached(
        "HandlesKeys", typeof(bool), typeof(ReorderListDrag), new PropertyMetadata(false, OnHandlesKeysChanged));

    public static readonly DependencyProperty IsRowProperty = DependencyProperty.RegisterAttached(
        "IsRow", typeof(bool), typeof(ReorderListDrag), new PropertyMetadata(false));

    // The row being dragged, per list; only one drag can be under way at a time.
    private static readonly DependencyProperty DraggedProperty = DependencyProperty.RegisterAttached(
        "Dragged", typeof(ReorderListItem), typeof(ReorderListDrag), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsHandle(DependencyObject element) => (bool)element.GetValue(IsHandleProperty);

    public static void SetIsHandle(DependencyObject element, bool value) => element.SetValue(IsHandleProperty, value);

    public static bool GetHandlesKeys(DependencyObject element) => (bool)element.GetValue(HandlesKeysProperty);

    public static void SetHandlesKeys(DependencyObject element, bool value) => element.SetValue(HandlesKeysProperty, value);

    public static bool GetIsRow(DependencyObject element) => (bool)element.GetValue(IsRowProperty);

    public static void SetIsRow(DependencyObject element, bool value) => element.SetValue(IsRowProperty, value);

    private static void OnHandlesKeysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list || e.NewValue is not true)
        {
            return;
        }

        list.PreviewKeyDown += OnKey;
        // handledEventsToo: a press on the grip is handled by the drag, and still selects the row.
        list.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPressSelect), handledEventsToo: true);
    }

    private static void OnPressSelect(object sender, MouseButtonEventArgs e)
    {
        if (RowOf(e.OriginalSource as DependencyObject, (DependencyObject)sender) is { } row)
        {
            row.Focus();
        }
    }

    private static void OnKey(object sender, KeyEventArgs e)
    {
        var list = (ItemsControl)sender;
        if (list.DataContext is not ReorderListSetting setting ||
            RowOf(Keyboard.FocusedElement as DependencyObject, list) is not { DataContext: ReorderListItem item })
        {
            return;
        }

        // Alt turns the arrow keys into Key.System.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        if (alt && key is Key.Up or Key.Down)
        {
            var from = setting.VisibleItems.IndexOf(item);
            var to = from + (key == Key.Up ? -1 : 1);
            if (from >= 0 && to >= 0 && to < setting.VisibleItems.Count)
            {
                setting.MoveVisibleLive(from, to);
                setting.CommitOrder();
                Refocus(list, item);
            }

            e.Handled = true;
        }
        else if (key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            item.Visible.Value = !item.Visible.Value;
            // It moves to the other list (shown / hidden); the selection follows it there.
            Refocus(list, item);
            e.Handled = true;
        }
    }

    /// <summary>Puts the keyboard back on <paramref name="item"/>'s row once the lists have redrawn,
    /// in whichever of the two lists it is now in.</summary>
    private static void Refocus(ItemsControl list, ReorderListItem item)
    {
        var scope = VisualTreeHelper.GetParent(list) ?? list;
        list.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (FindRow(scope, item) is { } row)
            {
                row.Focus();
                row.BringIntoView();
            }
        });
    }

    private static FrameworkElement? FindRow(DependencyObject node, ReorderListItem item)
    {
        if (node is FrameworkElement element && GetIsRow(element) && ReferenceEquals(element.DataContext, item))
        {
            return element;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (FindRow(VisualTreeHelper.GetChild(node, i), item) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static FrameworkElement? RowOf(DependencyObject? element, DependencyObject stop)
    {
        for (var node = element; node is not null && node != stop; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement row && GetIsRow(row))
            {
                return row;
            }
        }

        return null;
    }

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

        // The list dragged in shows only the rows switched on.
        var from = setting.VisibleItems.IndexOf(item);
        var to = IndexUnder(list, e.GetPosition(list).Y);
        if (to >= 0 && to != from)
        {
            setting.MoveVisibleLive(from, to);
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
