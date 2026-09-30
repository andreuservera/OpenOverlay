using System.Windows.Media;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// Code-behind twin of the semantic state colors in Themes/DesignTokens.xaml. Panels that repaint
/// per telemetry tick (shift lights, proximity bars, wear meters) can't afford a resource lookup on
/// every frame, so they take frozen brushes from here instead — keep the two files in step.
/// </summary>
internal static class StatePalette
{
    public static readonly Brush Positive = Frozen(0x34, 0xD3, 0x99);
    public static readonly Brush Negative = Frozen(0xFF, 0x6B, 0x6B);
    public static readonly Brush Warning = Frozen(0xF5, 0xA5, 0x24);
    public static readonly Brush Critical = Frozen(0xF0, 0x44, 0x38);
    public static readonly Brush Accent = Frozen(0xFF, 0xD2, 0x4D);
    public static readonly Brush Info = Frozen(0x6C, 0xB8, 0xFF);

    public static readonly Brush TextPrimary = Frozen(0xF2, 0xF5, 0xF8);
    public static readonly Brush TextMuted = Frozen(0x8E, 0x99, 0xA5);

    /// <summary>For text sitting on a saturated fill (car-number chips, map badges).</summary>
    public static readonly Brush TextOnAccent = Frozen(0x0A, 0x0C, 0x0F);

    /// <summary>Unfilled half of a gauge track, and the "off" state of an indicator segment.</summary>
    public static readonly Brush TrackEmpty = Frozen(0x23, 0x29, 0x31);

    public static readonly Color PositiveColor = Color.FromRgb(0x34, 0xD3, 0x99);
    public static readonly Color NegativeColor = Color.FromRgb(0xFF, 0x6B, 0x6B);

    /// <summary>"This is you" — same as State.SelfColor, the player's row in the tables.</summary>
    public static readonly Color SelfColor = Color.FromRgb(0x4C, 0x9A, 0xFF);

    private static Brush Frozen(byte r, byte g, byte b) => Frozen(0xFF, r, g, b);

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
