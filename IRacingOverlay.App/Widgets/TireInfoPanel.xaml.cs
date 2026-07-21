using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TireInfoPanel : UserControl
{
    public TireInfoPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(TireInfoState state)
    {
        SetCorner(LFPressureText, LFTempLeftText, LFTempMiddleText, LFTempRightText, state.LF);
        SetCorner(RFPressureText, RFTempLeftText, RFTempMiddleText, RFTempRightText, state.RF);
        SetCorner(LRPressureText, LRTempLeftText, LRTempMiddleText, LRTempRightText, state.LR);
        SetCorner(RRPressureText, RRTempLeftText, RRTempMiddleText, RRTempRightText, state.RR);
    }

    private static void SetCorner(
        TextBlock pressureText, TextBlock leftText, TextBlock middleText, TextBlock rightText, TireCornerInfo corner)
    {
        pressureText.Text = corner.PressureKPa > 0 ? $"{corner.PressureDisplay} kPa" : "—";
        leftText.Text = corner.TempLeftDisplay;
        middleText.Text = corner.TempMiddleDisplay;
        rightText.Text = corner.TempRightDisplay;
    }
}
