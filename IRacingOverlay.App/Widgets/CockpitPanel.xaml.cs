using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class CockpitPanel : UserControl
{
    private static readonly Brush ShiftOff = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly Brush ShiftGreen = new SolidColorBrush(Color.FromRgb(0x30, 0xC0, 0x30));
    private static readonly Brush ShiftYellow = new SolidColorBrush(Color.FromRgb(0xE0, 0xC0, 0x20));
    private static readonly Brush ShiftRed = new SolidColorBrush(Color.FromRgb(0xE0, 0x30, 0x30));

    private static readonly Brush AbsIdle = new SolidColorBrush(Color.FromRgb(0x2E, 0x1E, 0x1E));
    private static readonly Brush AbsBright = new SolidColorBrush(Color.FromRgb(0xFF, 0x30, 0x30));
    private static readonly Brush AbsDim = new SolidColorBrush(Color.FromRgb(0x80, 0x18, 0x18));

    private static readonly Brush TcIdle = new SolidColorBrush(Color.FromRgb(0x1E, 0x2E, 0x1E));
    private static readonly Brush TcBright = new SolidColorBrush(Color.FromRgb(0x30, 0xFF, 0x30));
    private static readonly Brush TcDim = new SolidColorBrush(Color.FromRgb(0x18, 0x80, 0x18));

    private Ellipse[] ShiftLights => [ShiftLight0, ShiftLight1, ShiftLight2, ShiftLight3, ShiftLight4];

    private bool _blinkPhase;

    public CockpitPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(CockpitState state)
    {
        GearText.Text = state.Gear;

        _blinkPhase = !_blinkPhase;

        var lights = ShiftLights;
        for (var i = 0; i < lights.Length; i++)
        {
            if (i >= state.ShiftLightsLit)
            {
                lights[i].Fill = ShiftOff;
                continue;
            }

            if (state.ShiftBlink && !_blinkPhase)
            {
                lights[i].Fill = ShiftOff;
                continue;
            }

            lights[i].Fill = i < 2 ? ShiftGreen : i < 4 ? ShiftYellow : ShiftRed;
        }

        AbsBar.Background = state.AbsActive ? (_blinkPhase ? AbsBright : AbsDim) : AbsIdle;
        TcBar.Background = state.TcActive ? (_blinkPhase ? TcBright : TcDim) : TcIdle;

        SetProximity(LeftProximityTrack, LeftProximityFill, state.LeftProximity);
        SetProximity(RightProximityTrack, RightProximityFill, state.RightProximity);
    }

    private static void SetProximity(Border track, Rectangle fill, double fraction)
    {
        var height = track.ActualHeight;
        fill.Height = height > 0 ? Math.Clamp(fraction, 0, 1) * height : 0;
    }
}
