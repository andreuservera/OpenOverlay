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
        LFCorner.Content = state.LF;
        RFCorner.Content = state.RF;
        LRCorner.Content = state.LR;
        RRCorner.Content = state.RR;
    }
}
