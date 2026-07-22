using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

/// <summary>Row of glowing dots — 5 green, 5 yellow, 4 red (14 total), matching the reference
/// dashboard design. Separate from ShiftGearPanel (the gear digit itself) because this row spans
/// wider than the gear digit alone in that design, sitting above the whole speed/gear/RPM cluster
/// rather than just above the gear number.</summary>
public partial class ShiftLightsPanel : UserControl
{
    private const int GreenCount = 5;
    private const int YellowCount = 5;

    private static readonly Brush Off = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x30, 0xC0, 0x30));
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(0xE0, 0xC0, 0x20));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xE0, 0x30, 0x30));

    private readonly Ellipse[] _dots = new Ellipse[CockpitState.ShiftLightCount];

    public ShiftLightsPanel()
    {
        InitializeComponent();

        for (var i = 0; i < _dots.Length; i++)
        {
            var dot = new Ellipse { Width = 14, Height = 14, Fill = Off, Margin = new System.Windows.Thickness(2) };
            LightsStack.Children.Add(dot);
            _dots[i] = dot;
        }
    }

    public void SetLit(int litCount, bool blink, bool blinkPhase)
    {
        for (var i = 0; i < _dots.Length; i++)
        {
            if (i >= litCount || (blink && !blinkPhase))
            {
                _dots[i].Fill = Off;
                continue;
            }

            _dots[i].Fill = i < GreenCount ? Green : i < GreenCount + YellowCount ? Yellow : Red;
        }
    }
}
