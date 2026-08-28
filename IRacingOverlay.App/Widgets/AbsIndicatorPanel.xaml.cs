using System.Windows.Controls;
using System.Windows.Media;

namespace IRacingOverlay.App.Widgets;

/// <summary>An outlined "ABS" telltale pill — dim gray outline/text normally, glows amber and
/// blinks while active, matching a real dash telltale.</summary>
public partial class AbsIndicatorPanel : UserControl
{
    // Idle is dim on purpose (it's the quiet state) but still legible: the old #3A/#55 pair sat
    // near 2:1 against the panel and read as a smudge rather than as a labelled telltale.
    private static readonly Brush IdleBorder = new SolidColorBrush(Color.FromRgb(0x4C, 0x51, 0x57));
    private static readonly Brush IdleText = new SolidColorBrush(Color.FromRgb(0x77, 0x80, 0x88));
    private static readonly Brush DimAmber = new SolidColorBrush(Color.FromRgb(0x8A, 0x60, 0x1E));
    private static readonly Brush BrightAmber = new SolidColorBrush(Color.FromRgb(0xFF, 0xB2, 0x38));

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
