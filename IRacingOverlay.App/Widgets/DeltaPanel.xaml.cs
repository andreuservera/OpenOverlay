using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class DeltaPanel : UserControl
{
    private static readonly Color Neutral = Color.FromRgb(0xFF, 0xFF, 0xFF); // no data at all
    private static readonly Color Green = Color.FromRgb(0x30, 0xE0, 0x30);
    private static readonly Color Red = Color.FromRgb(0xE8, 0x30, 0x30);

    public DeltaPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(DeltaState state)
    {
        LabelText.Text = state.ReferenceLabel;
        DeltaText.Text = state.Display;
        DeltaText.Foreground = new SolidColorBrush(!state.IsValid ? Neutral : ColorForRate(state.RateOfChange));
    }

    // Gaining on the reference lap -> green, losing -> red.
    private static Color ColorForRate(double rateOfChange) => rateOfChange < 0 ? Green : Red;
}
