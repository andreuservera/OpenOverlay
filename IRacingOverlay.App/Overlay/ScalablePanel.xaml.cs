using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// The single sizing standard for every widget and dashboard panel.
///
/// Content is laid out once at its design size (<see cref="DesignWidth"/>/<see cref="DesignHeight"/>,
/// or its natural measured size when those are left unset) and then scaled as a whole by one of five
/// fixed <see cref="ScaleLevel"/>s. Because the scale is a LayoutTransform, this control always
/// reports the *scaled* size to its parent, so the container tracks the content rather than the
/// other way round — a panel can never be given less room than it needs, which is exactly what
/// free-form drag-resizing used to allow (clipped text, overlapping rows, broken layouts).
///
/// Every proportion inside the content — padding, margins, gaps, type scale, stroke widths — is
/// multiplied by the same factor, so a widget looks identical at XS and XL apart from its size.
/// </summary>
[ContentProperty(nameof(ScalableContent))]
public partial class ScalablePanel : UserControl
{
    // Long enough to read as motion, short enough that repeated +/- presses still feel instant.
    private static readonly Duration TransitionDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(120));

    private bool _isHovered;
    private bool _isLoaded;

    public static readonly DependencyProperty ScalableContentProperty =
        DependencyProperty.Register(nameof(ScalableContent), typeof(object), typeof(ScalablePanel));

    public object? ScalableContent
    {
        get => GetValue(ScalableContentProperty);
        set => SetValue(ScalableContentProperty, value);
    }

    public static readonly DependencyProperty DesignWidthProperty =
        DependencyProperty.Register(nameof(DesignWidth), typeof(double), typeof(ScalablePanel),
            new PropertyMetadata(0.0));

    /// <summary>The content's design width at level M, in device-independent pixels; every other
    /// level is this times the level's factor. Set it on panels whose layout stretches to fill (a
    /// track map, a trace plot) — those have no meaningful natural width, and without a design size
    /// they collapse to their longest label. It is applied as a floor, not a fixed width: content
    /// that measures wider still gets the room it asks for, so declaring a design size can never be
    /// the thing that clips a panel. 0 (the default) means "just use the natural size".</summary>
    public double DesignWidth
    {
        get => (double)GetValue(DesignWidthProperty);
        set => SetValue(DesignWidthProperty, value);
    }

    public static readonly DependencyProperty DesignHeightProperty =
        DependencyProperty.Register(nameof(DesignHeight), typeof(double), typeof(ScalablePanel),
            new PropertyMetadata(0.0));

    /// <summary>The content's design height at level M. See <see cref="DesignWidth"/>.</summary>
    public double DesignHeight
    {
        get => (double)GetValue(DesignHeightProperty);
        set => SetValue(DesignHeightProperty, value);
    }

    public static readonly DependencyProperty ShowButtonsProperty =
        DependencyProperty.Register(nameof(ShowButtons), typeof(bool), typeof(ScalablePanel),
            new PropertyMetadata(true, OnShowButtonsChanged));

    /// <summary>Floating widgets are click-through while locked, so bind this to the widget's
    /// IsEditMode — the size control stays unreachable while racing regardless of hover. Dashboard
    /// panels leave it at the default. Either way the control only ever appears on hover (see
    /// <see cref="UpdateChromeVisibility"/>) so nothing sits on screen permanently.</summary>
    public bool ShowButtons
    {
        get => (bool)GetValue(ShowButtonsProperty);
        set => SetValue(ShowButtonsProperty, value);
    }

    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(ScaleLevel), typeof(ScalablePanel),
            new PropertyMetadata(ScaleLevels.Default, OnLevelChanged));

    /// <summary>The current step on the shared ladder. Setting it animates to the new size and, once
    /// the panel is loaded, persists it under <see cref="PersistenceKey"/>.</summary>
    public ScaleLevel Level
    {
        get => (ScaleLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>Key under which this panel's level is remembered across restarts via
    /// <see cref="ScaleLevelStore"/>. Null means "don't persist".</summary>
    public string? PersistenceKey { get; set; }

    public ScalablePanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        if (PersistenceKey is { } key)
        {
            Level = ScaleLevelStore.Get(key);
        }

        // The restored level has to land without a transition: animating on startup would show every
        // widget visibly inflating as the app opens.
        _isLoaded = true;
        ApplyLevel(animate: false);
        UpdateChromeState();
    }

    private static void OnShowButtonsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ScalablePanel)d).UpdateChromeVisibility();

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (ScalablePanel)d;
        if (!panel._isLoaded)
        {
            return;
        }

        panel.ApplyLevel(animate: true);
        panel.UpdateChromeState();

        if (panel.PersistenceKey is { } key)
        {
            ScaleLevelStore.Save(key, panel.Level);
        }
    }

    private void MinusButton_Click(object sender, RoutedEventArgs e) => Level = ScaleLevels.Smaller(Level);

    private void PlusButton_Click(object sender, RoutedEventArgs e) => Level = ScaleLevels.Larger(Level);

    private void LevelChip_Click(object sender, RoutedEventArgs e) => Level = ScaleLevels.Default;

    // Ctrl+wheel is the same gesture as zooming anywhere else, and it only bites while the size
    // control is available — a bare wheel still belongs to whatever is underneath.
    private void Root_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!ShowButtons || Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0)
        {
            return;
        }

        Level = e.Delta > 0 ? ScaleLevels.Larger(Level) : ScaleLevels.Smaller(Level);
        e.Handled = true;
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        _isHovered = true;
        UpdateChromeVisibility();
    }

    private void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        _isHovered = false;
        UpdateChromeVisibility();
    }

    /// <summary>
    /// Animates the LayoutTransform itself, so container and content grow together in one motion
    /// rather than the container snapping to its new size while the content catches up. Each tick is
    /// a layout pass over one small visual tree, and only ever on the panel under the pointer.
    /// </summary>
    private void ApplyLevel(bool animate)
    {
        var factor = ScaleLevels.FactorOf(Level);

        if (!animate)
        {
            ContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ContentScale.ScaleX = factor;
            ContentScale.ScaleY = factor;
            return;
        }

        // Ease-out only: a size change should look like it settles into place, not like it hesitates
        // first. No From — starting from the current (possibly mid-flight) value is what keeps rapid
        // +/- presses continuous instead of restarting each time from the last committed level.
        var animation = new DoubleAnimation(factor, TransitionDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        ContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        ContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void UpdateChromeState()
    {
        LevelChip.Content = ScaleLevels.LabelOf(Level);
        MinusButton.IsEnabled = !ScaleLevels.IsSmallest(Level);
        PlusButton.IsEnabled = !ScaleLevels.IsLargest(Level);
    }

    private void UpdateChromeVisibility()
    {
        var visible = ShowButtons && _isHovered;
        if (visible)
        {
            ScaleChrome.Visibility = Visibility.Visible;
        }

        // Stop taking clicks the instant it starts fading out, so a click during the fade reaches the
        // widget (or passes through to the game) instead of a control that is on its way out.
        ScaleChrome.IsHitTestVisible = visible;

        var fade = new DoubleAnimation(visible ? 1.0 : 0.0, FadeDuration);
        if (!visible)
        {
            fade.Completed += (_, _) =>
            {
                if (!ScaleChrome.IsHitTestVisible)
                {
                    ScaleChrome.Visibility = Visibility.Collapsed;
                }
            };
        }

        ScaleChrome.BeginAnimation(OpacityProperty, fade);
    }
}
