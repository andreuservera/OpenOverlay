using System.Windows;
using System.Windows.Controls;
using IRacingOverlay.App.Layouts;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// A read-only picture of a layout: what opening it shows on its monitor. Only the widgets that
/// take over when it opens are drawn (hidden ones don't), each by <see cref="EditorWidgetItem"/>, the
/// same item the editor's canvas uses, so both render a widget identically.
/// </summary>
public partial class LayoutPreview : UserControl
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(Layout), typeof(LayoutPreview),
        new PropertyMetadata(null, (d, _) => ((LayoutPreview)d).Rebuild()));

    public static readonly DependencyProperty EmptyMessageProperty = DependencyProperty.Register(
        nameof(EmptyMessage), typeof(string), typeof(LayoutPreview),
        new PropertyMetadata(null, (d, e) => ((LayoutPreview)d).EmptyText.Text = (string)e.NewValue));

    /// <summary>What the picture says when there is nothing in it.</summary>
    public string EmptyMessage
    {
        get => (string)GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    /// <summary>Margin around the frame, in screen pixels.</summary>
    private const double Inset = 24;

    public LayoutPreview()
    {
        InitializeComponent();
        EmptyText.Text = EmptyMessage;
        IsVisibleChanged += (_, _) => FitWhenLaidOut();
    }

    public Layout? Layout
    {
        get => (Layout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    private void Rebuild()
    {
        if (Layout is not { } layout)
        {
            // Dropped when the page closes, so no panel outlives it.
            Items.ItemsSource = null;
            return;
        }

        Surface.Width = layout.Width;
        Surface.Height = layout.Height;
        var items = layout.ControlledBottomToTop().Select(widget => new EditorWidgetItem(widget)).ToList();
        Items.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Fit();
        FitWhenLaidOut();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Fit();

    /// <summary>Fits again once laid out. A preview shown again at the size it had before it was hidden
    /// raises no SizeChanged, so a layout handed to it while hidden would otherwise stay unscaled.</summary>
    private void FitWhenLaidOut() => Dispatcher.BeginInvoke(Fit, System.Windows.Threading.DispatcherPriority.Loaded);

    /// <summary>Scales the monitor to fit the pane, never above its real size.</summary>
    private void Fit()
    {
        if (Layout is not { } layout || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var scale = Math.Clamp(
            Math.Min((ActualWidth - 2 * Inset) / layout.Width, (ActualHeight - 2 * Inset) / layout.Height),
            0.01,
            1);
        FrameScale.ScaleX = scale;
        FrameScale.ScaleY = scale;
        // A one-pixel line on screen, whatever the scale.
        Frame.BorderThickness = new Thickness(1 / scale);
    }
}
