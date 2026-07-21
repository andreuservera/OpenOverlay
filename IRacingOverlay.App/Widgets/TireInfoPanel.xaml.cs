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
        SetCorner(LFPressureText, LFTempText, state.LF);
        SetCorner(RFPressureText, RFTempText, state.RF);
        SetCorner(LRPressureText, LRTempText, state.LR);
        SetCorner(RRPressureText, RRTempText, state.RR);
    }

    private static void SetCorner(TextBlock pressureText, TextBlock tempText, TireCornerInfo corner)
    {
        pressureText.Text = corner.PressureKPa > 0 ? $"{corner.PressureDisplay} kPa" : "—";
        tempText.Text = corner.TempC > 0 ? $"{corner.TempDisplay}C" : "—";
    }
}
