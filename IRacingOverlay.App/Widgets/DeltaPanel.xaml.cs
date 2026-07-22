using System.Windows.Controls;
using System.Windows.Media;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Widgets;

public partial class DeltaPanel : UserControl
{
    private static readonly Color Neutral = Color.FromRgb(0xFF, 0xFF, 0xFF); // no data at all
    private static readonly Color Steady = Color.FromRgb(0x90, 0x90, 0x90); // rate of change ~= 0
    private static readonly Color FullGreen = Color.FromRgb(0x30, 0xE0, 0x30);
    private static readonly Color FullRed = Color.FromRgb(0xE8, 0x30, 0x30);

    // Rate of change (seconds of gap per second of real time) at which the color reaches full
    // saturation — beyond this it's already a dramatic swing, no need to distinguish further.
    private const double FullIntensityRate = 0.5;

    public DeltaPanel()
    {
        InitializeComponent();
    }

    public void UpdateState(DeltaState state)
    {
        LabelText.Text = state.ReferenceLabel;
        DeltaText.Text = state.Display;
        DeltaText.Foreground = new SolidColorBrush(!state.IsValid ? Neutral : ColorForRate(state.RateOfChange));
    }

    /// <summary>Hue comes from the SIGN of the rate of change (gaining -> green, losing -> red,
    /// holding steady -> gray), intensity from its MAGNITUDE — this shows the driver's current trend
    /// against the reference lap, not just where they stand right now (the number already shows that).</summary>
    private static Color ColorForRate(double rateOfChange)
    {
        var target = rateOfChange < 0 ? FullGreen : FullRed;
        var intensity = Math.Clamp(Math.Abs(rateOfChange) / FullIntensityRate, 0, 1);
        return Lerp(Steady, target, intensity);
    }

    private static Color Lerp(Color from, Color to, double t) => Color.FromRgb(
        (byte)(from.R + (to.R - from.R) * t),
        (byte)(from.G + (to.G - from.G) * t),
        (byte)(from.B + (to.B - from.B) * t));
}
