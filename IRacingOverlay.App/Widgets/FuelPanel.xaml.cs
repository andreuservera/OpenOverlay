using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class FuelPanel : UserControl
{
    private static readonly Brush Ample = new SolidColorBrush(Color.FromRgb(0xFF, 0xB2, 0x38));
    private static readonly Brush Short = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D));

    public FuelPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(FuelState state)
    {
        LapsOfFuelText.Text = state.LapsOfFuelDisplay;
        LevelText.Text = state.LevelDisplay;
        PerLapText.Text = state.PerLapDisplay;

        var trackWidth = LevelTrack.ActualWidth;
        LevelFill.Width = trackWidth > 0 ? Math.Clamp(state.LevelPct, 0, 1) * trackWidth : 0;

        // Only color-flag "not enough fuel" when we can actually compare both sides — otherwise
        // (practice/timed sessions with no lap limit) this is just informational, not a warning.
        LapsOfFuelText.Foreground = state.WillMakeItToTheEnd == false ? Short : Ample;
    }
}
