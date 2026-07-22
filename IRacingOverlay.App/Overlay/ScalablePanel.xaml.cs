using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// Wraps arbitrary panel content with a +/- button pair (bottom-right, next to where a resize grip
/// would sit) that scales the content via a LayoutTransform — one reusable control instead of adding
/// near-identical font-size machinery to every widget/dashboard panel individually.
/// </summary>
[ContentProperty(nameof(ScalableContent))]
public partial class ScalablePanel : UserControl
{
    private const double MinScale = 0.6;
    private const double MaxScale = 2.5;
    private const double ScaleStep = 0.1;

    private bool _isHovered;

    public static readonly DependencyProperty ScalableContentProperty =
        DependencyProperty.Register(nameof(ScalableContent), typeof(object), typeof(ScalablePanel));

    public object? ScalableContent
    {
        get => GetValue(ScalableContentProperty);
        set => SetValue(ScalableContentProperty, value);
    }

    public static readonly DependencyProperty ShowButtonsProperty =
        DependencyProperty.Register(nameof(ShowButtons), typeof(bool), typeof(ScalablePanel),
            new PropertyMetadata(true, OnShowButtonsChanged));

    /// <summary>Floating widgets are click-through while locked, so bind this to the widget's
    /// IsEditMode — buttons stay unreachable while locked regardless of hover. Dashboard panels
    /// leave this at the default (always allowed). Either way the buttons only ever actually appear
    /// on mouse hover (see <see cref="UpdateButtonsVisibility"/>) so nothing sits on screen
    /// permanently.</summary>
    public bool ShowButtons
    {
        get => (bool)GetValue(ShowButtonsProperty);
        set => SetValue(ShowButtonsProperty, value);
    }

    /// <summary>Key under which this panel's scale is remembered across restarts via
    /// PanelScaleStore. Null means "don't persist."</summary>
    public string? PersistenceKey { get; set; }

    public ScalablePanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (PersistenceKey is { } key)
            {
                ApplyScale(PanelScaleStore.Get(key));
            }
        };
    }

    private static void OnShowButtonsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ScalablePanel)d).UpdateButtonsVisibility();

    private void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        _isHovered = true;
        UpdateButtonsVisibility();
    }

    private void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        _isHovered = false;
        UpdateButtonsVisibility();
    }

    private void UpdateButtonsVisibility() =>
        ButtonsPanel.Visibility = ShowButtons && _isHovered ? Visibility.Visible : Visibility.Collapsed;

    private void MinusButton_Click(object sender, RoutedEventArgs e) => AdjustScale(-ScaleStep);

    private void PlusButton_Click(object sender, RoutedEventArgs e) => AdjustScale(ScaleStep);

    private void AdjustScale(double delta)
    {
        var next = Math.Clamp(ScaleTransform.ScaleX + delta, MinScale, MaxScale);
        ApplyScale(next);
        if (PersistenceKey is { } key)
        {
            PanelScaleStore.Save(key, next);
        }
    }

    private void ApplyScale(double scale)
    {
        var clamped = Math.Clamp(scale, MinScale, MaxScale);
        ScaleTransform.ScaleX = clamped;
        ScaleTransform.ScaleY = clamped;
    }
}
