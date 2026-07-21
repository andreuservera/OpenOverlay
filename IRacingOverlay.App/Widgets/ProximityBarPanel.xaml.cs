using System.Windows.Controls;

namespace IRacingOverlay.App.Widgets;

/// <summary>Generic vertical fill bar for "how alongside is the nearest car on this side."</summary>
public partial class ProximityBarPanel : UserControl
{
    public ProximityBarPanel()
    {
        InitializeComponent();
    }

    public void SetFraction(double fraction)
    {
        var height = Track.ActualHeight;
        Fill.Height = height > 0 ? Math.Clamp(fraction, 0, 1) * height : 0;
    }
}
