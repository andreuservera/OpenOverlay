using System.Windows.Controls;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets;

/// <summary>An outlined "ABS" telltale pill — dim gray outline/text normally, glows amber and
/// blinks while active, matching a real dash telltale.</summary>
public partial class AbsIndicatorPanel : UserControl
{
    private static readonly Brush IdleBorder = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
    private static readonly Brush IdleText = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush DimAmber = new SolidColorBrush(Color.FromRgb(0x80, 0x58, 0x00));
    private static readonly Brush BrightAmber = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x00));

    public AbsIndicatorPanel()
    {
        InitializeComponent();
        Light.BorderBrush = IdleBorder;
        Label.Foreground = IdleText;
    }

    /// <summary>blinkPhase is supplied by the caller (toggled once per tick) so this stays in sync
    /// with any other indicator that also flashes.</summary>
    public void SetActive(bool active, bool blinkPhase)
    {
        if (!active)
        {
            Light.BorderBrush = IdleBorder;
            Label.Foreground = IdleText;
            return;
        }

        var color = blinkPhase ? BrightAmber : DimAmber;
        Light.BorderBrush = color;
        Label.Foreground = color;
    }
}
