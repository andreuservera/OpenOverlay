using System.Windows;
using System.Windows.Controls;

namespace IRacingOverlay.App.Widgets;

/// <summary>A StackPanel whose spacing only goes between visible children, so hiding an element
/// closes the gap instead of leaving its margin behind.</summary>
public sealed class SpacedStackPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(SpacedStackPanel),
        new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(SpacedStackPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private bool Horizontal => Orientation == Orientation.Horizontal;

    protected override Size MeasureOverride(Size availableSize)
    {
        var childAvailable = Horizontal
            ? new Size(double.PositiveInfinity, availableSize.Height)
            : new Size(availableSize.Width, double.PositiveInfinity);
        double along = 0, across = 0;
        var visible = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(childAvailable);
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var size = child.DesiredSize;
            along += (visible++ > 0 ? Spacing : 0) + (Horizontal ? size.Width : size.Height);
            across = Math.Max(across, Horizontal ? size.Height : size.Width);
        }

        return Horizontal ? new Size(along, across) : new Size(across, along);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double offset = 0;
        var visible = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            offset += visible++ > 0 ? Spacing : 0;
            var size = child.DesiredSize;
            if (Horizontal)
            {
                child.Arrange(new Rect(offset, 0, size.Width, finalSize.Height));
                offset += size.Width;
            }
            else
            {
                child.Arrange(new Rect(0, offset, finalSize.Width, size.Height));
                offset += size.Height;
            }
        }

        return finalSize;
    }
}
