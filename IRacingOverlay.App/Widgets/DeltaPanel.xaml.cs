using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class DeltaPanel : UserControl
{
    private static readonly Brush Neutral = StatePalette.TextPrimary; // no data at all
    private static readonly Brush Green = StatePalette.Positive;
    private static readonly Brush Red = StatePalette.Negative;
    private static readonly Brush GainWash = Wash(StatePalette.PositiveColor);
    private static readonly Brush LossWash = Wash(StatePalette.NegativeColor);

    public DeltaPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(DeltaState state)
    {
        LabelText.Text = state.ReferenceLabel;
        DeltaText.Text = state.Display;
        DeltaText.Foreground = !state.IsValid ? Neutral : BrushForRate(state.RateOfChange);
        StateWash.Background = !state.IsValid ? null : state.RateOfChange < 0 ? GainWash : LossWash;
    }

    // Gaining on the reference lap -> green, losing -> red.
    private static Brush BrushForRate(double rateOfChange) => rateOfChange < 0 ? Green : Red;

    private static Brush Wash(Color color)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.35),
                new GradientStop(Color.FromArgb(0x33, color.R, color.G, color.B), 1),
            },
        };
        brush.Freeze();
        return brush;
    }
}
