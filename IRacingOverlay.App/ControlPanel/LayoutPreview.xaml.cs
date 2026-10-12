using System.Globalization;
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

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(LayoutPreview),
        new PropertyMetadata(false, (d, _) => ((LayoutPreview)d).ApplyCompact()));

    /// <summary>Margin around the frame, in screen pixels.</summary>
    private double Inset => IsCompact ? 8 : 24;

    public LayoutPreview()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => FitWhenLaidOut();
    }

    public Layout? Layout
    {
        get => (Layout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>A thumbnail (a Layouts card): a thinner margin, and no resolution badge or empty-layout
    /// note, which the card says in words beside it.</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private void ApplyCompact()
    {
        ResolutionBadge.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        Rebuild();
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
        ResolutionText.Text = string.Create(CultureInfo.InvariantCulture, $"{layout.Width}×{layout.Height}");
        var items = layout.ControlledBottomToTop().Select(widget => new EditorWidgetItem(widget)).ToList();
        Items.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 && !IsCompact ? Visibility.Visible : Visibility.Collapsed;
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
