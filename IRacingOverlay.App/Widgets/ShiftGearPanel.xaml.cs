using System.Windows.Controls;

namespace IRacingOverlay.App.Widgets;

/// <summary>Just the gear digit and its flanking accent brackets/label — the shift-light dot row
/// lives in the separate ShiftLightsPanel, since in the reference design that row spans wider than
/// the gear digit alone (above the whole speed/gear/RPM cluster).</summary>
public partial class ShiftGearPanel : UserControl
{
    public ShiftGearPanel()
    {
        InitializeComponent();
    }

    public void SetGear(string gear) => GearText.Text = gear;
}
