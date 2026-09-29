using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class PedalTracePanel : UserControl
{
    public PedalTracePanel()
    {
        InitializeComponent();
    }

    public void UpdateState(PedalTraceState state)
    {
        SetBar(ThrottleFill, state.Throttle);
        SetBar(BrakeFill, state.Brake);
        SetBar(ClutchFill, state.Clutch);
        Trace.SetTrace(state);
    }

    // Scaled rather than resized, so a pedal moving every frame never triggers a layout pass.
    private static void SetBar(Rectangle fill, double value) =>
        ((ScaleTransform)fill.RenderTransform).ScaleY = Math.Clamp(value, 0, 1);
}
