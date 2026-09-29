using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Widgets;

/// <summary>The gear digit on its tile. The shift-light strip lives in ShiftLightsPanel, since it
/// spans the whole speed/gear/RPM cluster.</summary>
public partial class ShiftGearPanel : UserControl
{
    private static readonly Brush IdleEdge = StatePalette.ChipEdge;
    private static readonly Brush ShiftEdge = StatePalette.Critical;

    public ShiftGearPanel()
    {
        InitializeComponent();
    }

    public void SetGear(string gear) => GearText.Text = gear;

    /// <summary>Red edge on the tile while the shift lights are flashing: the cue stays on the digit
    /// the eye is already on.</summary>
    public void SetShiftPoint(bool atShiftPoint) => Tile.BorderBrush = atShiftPoint ? ShiftEdge : IdleEdge;
}
