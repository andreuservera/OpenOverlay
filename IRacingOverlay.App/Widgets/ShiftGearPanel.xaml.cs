using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IRacingOverlay.App.Widgets;

public partial class ShiftGearPanel : UserControl
{
    private static readonly Brush ShiftOff = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly Brush ShiftGreen = new SolidColorBrush(Color.FromRgb(0x30, 0xC0, 0x30));
    private static readonly Brush ShiftYellow = new SolidColorBrush(Color.FromRgb(0xE0, 0xC0, 0x20));
    private static readonly Brush ShiftRed = new SolidColorBrush(Color.FromRgb(0xE0, 0x30, 0x30));

    private Ellipse[] ShiftLights => [ShiftLight0, ShiftLight1, ShiftLight2, ShiftLight3, ShiftLight4];

    public ShiftGearPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(string gear, int litCount, bool shiftBlink, bool blinkPhase)
    {
        GearText.Text = gear;

        var lights = ShiftLights;
        for (var i = 0; i < lights.Length; i++)
        {
            if (i >= litCount)
            {
                lights[i].Fill = ShiftOff;
                continue;
            }

            if (shiftBlink && !blinkPhase)
            {
                lights[i].Fill = ShiftOff;
                continue;
            }

            lights[i].Fill = i < 2 ? ShiftGreen : i < 4 ? ShiftYellow : ShiftRed;
        }
    }
}
