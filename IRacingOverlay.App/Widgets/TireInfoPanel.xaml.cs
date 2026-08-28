using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.Overlay;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TireInfoPanel : UserControl
{
    private static readonly Brush WearGood = StatePalette.Positive;
    private static readonly Brush WearWarning = StatePalette.Warning;
    private static readonly Brush WearCritical = StatePalette.Critical;

    public TireInfoPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(TireInfoState state)
    {
        SetCorner(LFPressureText, LFTempLeftText, LFTempMiddleText, LFTempRightText, LFWearTrack, LFWearFill, state.LF);
        SetCorner(RFPressureText, RFTempLeftText, RFTempMiddleText, RFTempRightText, RFWearTrack, RFWearFill, state.RF);
        SetCorner(LRPressureText, LRTempLeftText, LRTempMiddleText, LRTempRightText, LRWearTrack, LRWearFill, state.LR);
        SetCorner(RRPressureText, RRTempLeftText, RRTempMiddleText, RRTempRightText, RRWearTrack, RRWearFill, state.RR);
    }

    private static void SetCorner(
        TextBlock pressureText, TextBlock leftText, TextBlock middleText, TextBlock rightText,
        Border wearTrack, Rectangle wearFill, TireCornerInfo corner)
    {
        pressureText.Text = corner.PressureKPa > 0 ? $"{corner.PressureDisplay} kPa" : "—";
        leftText.Text = corner.TempLeftDisplay;
        middleText.Text = corner.TempMiddleDisplay;
        rightText.Text = corner.TempRightDisplay;

        if (!corner.HasWearData)
        {
            wearFill.Width = 0;
            return;
        }

        var fraction = Math.Clamp(corner.WorstWearFraction, 0, 1);
        var trackWidth = wearTrack.ActualWidth;
        wearFill.Width = trackWidth > 0 ? fraction * trackWidth : 0;
        wearFill.Fill = fraction switch
        {
            > 0.6 => WearGood,
            > 0.3 => WearWarning,
            _ => WearCritical,
        };
    }
}
