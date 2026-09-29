using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.Overlay;

namespace IRacingOverlay.App.Widgets;

/// <summary>An outlined "ABS" telltale pill — dim gray outline/text normally, glows amber and
/// blinks while active, matching a real dash telltale.</summary>
public partial class AbsIndicatorPanel : UserControl
{
    // Idle is quiet on purpose but still legible as a labelled telltale. Active lights the outline
    // and text amber over a faint amber wash, the way a real dash lamp glows through its lens.
    private static readonly Brush IdleBorder = StatePalette.Outline;
    private static readonly Brush IdleText = StatePalette.TextMuted;
    private static readonly Brush DimAmber = Frozen(Color.FromRgb(0x8A, 0x5D, 0x18));
    private static readonly Brush BrightAmber = StatePalette.Warning;
    private static readonly Brush AmberWash = Frozen(Color.FromArgb(0x2E, 0xF5, 0xA5, 0x24));

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
            Light.Background = Brushes.Transparent;
            Label.Foreground = IdleText;
            return;
        }

        var color = blinkPhase ? BrightAmber : DimAmber;
        Light.BorderBrush = color;
        Light.Background = AmberWash;
        Label.Foreground = color;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
