using System.Windows;
using System.Windows.Controls;

namespace IRacingOverlay.App.ControlPanel;

/// <summary>
/// A setting row's two parts — its label (first child) and its control (second) — side by side
/// while the label keeps a readable width, and the control dropped under the label once it
/// wouldn't. The control panel's pages are wide enough for the first; the layout editor's narrow
/// settings column used to squeeze a slider row's label to a letter or two per line.
/// </summary>
public sealed class SettingRowPanel : Panel
{
    /// <summary>The narrowest the label may get beside its control.</summary>
    private const double MinLabelWidth = 150;

    /// <summary>Room between label and control, beside or above it.</summary>
    private const double Gap = 20;

    private const double StackGap = 8;

    private bool _stacked;

    private UIElement? Label => InternalChildren.Count > 0 ? InternalChildren[0] : null;

    private UIElement? Control => InternalChildren.Count > 1 ? InternalChildren[1] : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Label is not { } label || Control is not { } control)
        {
            return default;
        }

        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var controlSize = control.DesiredSize;
        _stacked = !double.IsInfinity(availableSize.Width)
            && availableSize.Width - controlSize.Width - Gap < MinLabelWidth;

        if (!_stacked)
        {
            label.Measure(new Size(Math.Max(0, availableSize.Width - controlSize.Width - Gap), double.PositiveInfinity));
            var width = double.IsInfinity(availableSize.Width)
                ? label.DesiredSize.Width + Gap + controlSize.Width
                : availableSize.Width;
            return new Size(width, Math.Max(label.DesiredSize.Height, controlSize.Height));
        }

        label.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        control.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(
            availableSize.Width,
            label.DesiredSize.Height + StackGap + control.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Label is not { } label || Control is not { } control)
        {
            return finalSize;
        }

        var controlSize = control.DesiredSize;
        if (!_stacked)
        {
            var labelWidth = Math.Max(0, finalSize.Width - controlSize.Width - Gap);
            label.Arrange(new Rect(0, (finalSize.Height - label.DesiredSize.Height) / 2, labelWidth, label.DesiredSize.Height));
            control.Arrange(new Rect(finalSize.Width - controlSize.Width, (finalSize.Height - controlSize.Height) / 2,
                controlSize.Width, controlSize.Height));
            return finalSize;
        }

        label.Arrange(new Rect(0, 0, finalSize.Width, label.DesiredSize.Height));
        control.Arrange(new Rect(0, label.DesiredSize.Height + StackGap,
            Math.Min(finalSize.Width, controlSize.Width), controlSize.Height));
        return finalSize;
    }
}
