using System.Windows.Controls;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class RadarPanel : UserControl
{
    public RadarPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(RadarState state) => View.Update(state);
}
