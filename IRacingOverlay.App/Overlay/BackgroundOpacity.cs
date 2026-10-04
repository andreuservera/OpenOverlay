using System.Windows;
using System.Windows.Media;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// A widget's opacity setting, applied to its background only: the panel frame
/// (<c>OverlayPanel</c>'s background and border) and the inset wells some panels draw on it fade,
/// while the numbers, labels, bars and icons on top stay fully readable.
///
/// Works by overriding those brushes in the element's own resources with faded copies of whatever
/// it would otherwise inherit, so every panel that draws its background through them
/// (<c>DynamicResource</c>) follows without knowing the feature exists. Set it on a widget window,
/// or on the presenter a preview draws a panel in.
/// </summary>
public static class BackgroundOpacity
{
    /// <summary>The brushes that make up a widget's background.</summary>
    private static readonly string[] Keys = ["Theme.PanelBackground", "Theme.PanelBorder", "Surface.Inset"];

    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(double), typeof(BackgroundOpacity), new PropertyMetadata(1.0, OnValueChanged));

    public static double GetValue(DependencyObject element) => (double)element.GetValue(ValueProperty);

    public static void SetValue(DependencyObject element, double value) => element.SetValue(ValueProperty, value);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var opacity = Math.Clamp((double)e.NewValue, 0, 1);
        foreach (var key in Keys)
        {
            // Dropped first, so the lookup below finds the brush this element would inherit
            // rather than its own earlier faded copy.
            element.Resources.Remove(key);
            if (opacity >= 1 || element.TryFindResource(key) is not Brush inherited)
            {
                continue;
            }

            var faded = inherited.Clone();
            faded.Opacity = inherited.Opacity * opacity;
            faded.Freeze();
            element.Resources[key] = faded;
        }
    }
}
