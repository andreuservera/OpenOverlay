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

    public DeltaPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(DeltaState state)
    {
        LabelText.Text = state.ReferenceLabel;
        DeltaText.Text = state.Display;
        DeltaText.Foreground = !state.IsValid ? Neutral : BrushForRate(state.RateOfChange);
    }

    // Gaining on the reference lap -> green, losing -> red.
    private static Brush BrushForRate(double rateOfChange) => rateOfChange < 0 ? Green : Red;
}
