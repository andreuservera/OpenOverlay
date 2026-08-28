using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class FuelPanel : UserControl
{
    private static readonly Brush Ample = StatePalette.TextPrimary;
    private static readonly Brush Short = StatePalette.Critical;
    private static readonly Brush LevelNormal = StatePalette.Warning;

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
        // The number stays plain white until then: an always-amber readout would train the eye to
        // ignore the one color that's supposed to mean "act now".
        var willRunShort = state.WillMakeItToTheEnd == false;
        LapsOfFuelText.Foreground = willRunShort ? Short : Ample;
        LevelFill.Fill = willRunShort ? Short : LevelNormal;
    }
}
