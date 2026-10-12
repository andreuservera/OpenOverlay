using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// Drag-and-drop for an <see cref="InfoBarSetting"/>'s diagram, by plain mouse capture like
/// <see cref="ReorderListDrag"/>: press on a chip, move, and the slot under the pointer lights;
/// release over a slot puts the chip there, over the tray takes it off the table. Set
/// <c>IsEnabled</c> on the diagram's root (whose DataContext is the setting) and <c>Target</c> on each
/// slot (an <see cref="InfoBarSlot"/>) and on the tray (<see cref="Tray"/>).
/// </summary>
public static class InfoBarDrag
{
    /// <summary>The <c>Target</c> of the tray of hidden info.</summary>
    public static readonly object Tray = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(InfoBarDrag), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(object), typeof(InfoBarDrag), new PropertyMetadata(null));

    /// <summary>Set on a target while a chip is held over it, for its template to light it.</summary>
    public static readonly DependencyProperty IsDragOverProperty = DependencyProperty.RegisterAttached(
        "IsDragOver", typeof(bool), typeof(InfoBarDrag), new PropertyMetadata(false));

    /// <summary>Set on a chip's ✕: pressing it takes the chip off the table.</summary>
    public static readonly DependencyProperty RemovesProperty = DependencyProperty.RegisterAttached(
        "Removes", typeof(bool), typeof(InfoBarDrag), new PropertyMetadata(false));

    public static bool GetRemoves(DependencyObject element) => (bool)element.GetValue(RemovesProperty);

    public static void SetRemoves(DependencyObject element, bool value) => element.SetValue(RemovesProperty, value);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(DragState), typeof(InfoBarDrag), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static object? GetTarget(DependencyObject element) => element.GetValue(TargetProperty);

    public static void SetTarget(DependencyObject element, object? value) => element.SetValue(TargetProperty, value);

    public static bool GetIsDragOver(DependencyObject element) => (bool)element.GetValue(IsDragOverProperty);

    public static void SetIsDragOver(DependencyObject element, bool value) => element.SetValue(IsDragOverProperty, value);

    private sealed class DragState(InfoChip chip, Point start)
    {
        public InfoChip Chip { get; } = chip;
        public Point Start { get; } = start;
        public bool Dragging { get; set; }
        public FrameworkElement? Over { get; set; }
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement root || e.NewValue is not true)
        {
            return;
        }

        root.PreviewMouseLeftButtonDown += OnPress;
        root.PreviewMouseMove += OnMove;
        root.PreviewMouseLeftButtonUp += OnRelease;
        root.LostMouseCapture += (_, _) => End(root);
    }

    private static void OnPress(object sender, MouseButtonEventArgs e)
    {
        var root = (FrameworkElement)sender;
        if (ChipAt(e.OriginalSource as DependencyObject, root) is not { } chip)
        {
            return;
        }

        // The chip's ✕ takes it off the table there and then.
        if (Ancestor<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource as DependencyObject, root) is { } button)
        {
            if (GetRemoves(button) && root.DataContext is InfoBarSetting setting)
            {
                setting.Move(chip, null);
                e.Handled = true;
            }

            return;
        }

        root.SetValue(StateProperty, new DragState(chip, e.GetPosition(root)));
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        var root = (FrameworkElement)sender;
        if (root.GetValue(StateProperty) is not DragState state || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(root);
        if (!state.Dragging)
        {
            if (Math.Abs(position.X - state.Start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - state.Start.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            state.Dragging = true;
            root.CaptureMouse();
            root.Cursor = Cursors.SizeAll;
        }

        var over = TargetAt(root, position);
        if (!ReferenceEquals(over, state.Over))
        {
            if (state.Over is not null)
            {
                SetIsDragOver(state.Over, false);
            }

            state.Over = over;
            if (over is not null)
            {
                SetIsDragOver(over, true);
            }
        }

        e.Handled = true;
    }

    private static void OnRelease(object sender, MouseButtonEventArgs e)
    {
        var root = (FrameworkElement)sender;
        if (root.GetValue(StateProperty) is not DragState { Dragging: true } state)
        {
            root.ClearValue(StateProperty);
            return;
        }

        var target = state.Over is null ? null : GetTarget(state.Over);
        End(root);
        if (root.DataContext is InfoBarSetting setting && target is not null)
        {
            setting.Move(state.Chip, target is InfoBarSlot slot ? slot.Slot : null);
        }

        e.Handled = true;
    }

    private static void End(FrameworkElement root)
    {
        if (root.GetValue(StateProperty) is DragState { Over: { } over })
        {
            SetIsDragOver(over, false);
        }

        root.ClearValue(StateProperty);
        root.ClearValue(FrameworkElement.CursorProperty);
        if (root.IsMouseCaptured)
        {
            root.ReleaseMouseCapture();
        }
    }

    private static FrameworkElement? TargetAt(FrameworkElement root, Point position)
    {
        for (var node = root.InputHitTest(position) as DependencyObject; node is not null && !ReferenceEquals(node, root); node = Parent(node))
        {
            if (node is FrameworkElement element && GetTarget(element) is not null)
            {
                return element;
            }
        }

        return null;
    }

    private static InfoChip? ChipAt(DependencyObject? node, DependencyObject root)
    {
        for (; node is not null && !ReferenceEquals(node, root); node = Parent(node))
        {
            if (node is FrameworkElement { DataContext: InfoChip chip })
            {
                return chip;
            }
        }

        return null;
    }

    private static T? Ancestor<T>(DependencyObject? node, DependencyObject root) where T : DependencyObject
    {
        for (; node is not null && !ReferenceEquals(node, root); node = Parent(node))
        {
            if (node is T found)
            {
                return found;
            }
        }

        return null;
    }

    private static DependencyObject? Parent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
