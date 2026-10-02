using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class TireInfoPanel : UserControl
{
    public TireInfoPanel()
    {
        InitializeComponent();
        UpdateState(TireInfoState.Empty);
    }

    public void UpdateState(TireInfoState state)
    {
        Show(LFCorner, state.LF);
        Show(RFCorner, state.RF);
        Show(LRCorner, state.LR);
        Show(RRCorner, state.RR);
    }

    // iRacing only refreshes these figures in the pit stall, but a new corner object arrives every
    // tick; handing it over anyway rebinds and redraws the card for nothing. Compared on exactly what
    // CornerTemplate shows, so a corner whose numbers do change is never left stale.
    private static void Show(ContentControl slot, TireCornerInfo next)
    {
        if (slot.Content is TireCornerInfo shown && SameDisplay(shown, next))
        {
            return;
        }

        slot.Content = next;
    }

    private static bool SameDisplay(TireCornerInfo a, TireCornerInfo b) =>
        a.Label == b.Label
        && a.HasPressure == b.HasPressure
        && a.PressureDisplay == b.PressureDisplay
        && a.PressureUnit == b.PressureUnit
        && a.TempLeftDisplay == b.TempLeftDisplay
        && a.TempMiddleDisplay == b.TempMiddleDisplay
        && a.TempRightDisplay == b.TempRightDisplay
        && a.WearLeftDisplay == b.WearLeftDisplay
        && a.WearMiddleDisplay == b.WearMiddleDisplay
        && a.WearRightDisplay == b.WearRightDisplay
        && a.WearLeftColor == b.WearLeftColor
        && a.WearMiddleColor == b.WearMiddleColor
        && a.WearRightColor == b.WearRightColor
        && a.WearLeftFill == b.WearLeftFill
        && a.WearMiddleFill == b.WearMiddleFill
        && a.WearRightFill == b.WearRightFill;
}
