using System.Windows.Media;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// Code-behind twin of the semantic state colors in Themes/DesignTokens.xaml. Panels that repaint
/// per telemetry tick (shift lights, proximity bars, wear meters) can't afford a resource lookup on
/// every frame, so they take frozen brushes from here instead — keep the two files in step.
/// </summary>
internal static class StatePalette
{
    public static readonly Brush Positive = Frozen(0x3D, 0xDC, 0x7A);
    public static readonly Brush Negative = Frozen(0xFF, 0x5A, 0x5A);
    public static readonly Brush Warning = Frozen(0xFF, 0xB2, 0x38);
    public static readonly Brush Critical = Frozen(0xFF, 0x3B, 0x30);
    public static readonly Brush Accent = Frozen(0xFF, 0xD2, 0x4D);
    public static readonly Brush Info = Frozen(0x8F, 0xD3, 0xFF);

    public static readonly Brush TextPrimary = Frozen(0xFF, 0xFF, 0xFF);
    public static readonly Brush TextMuted = Frozen(0x9B, 0xA5, 0xAE);

    /// <summary>For text sitting on a saturated fill (car-number chips, map badges).</summary>
    public static readonly Brush TextOnAccent = Frozen(0x0B, 0x0B, 0x0B);

    /// <summary>Unfilled half of a gauge track, and the "off" state of an indicator segment.</summary>
    public static readonly Brush TrackEmpty = Frozen(0x33, 0x38, 0x3D);

    /// <summary>Same role as <see cref="TrackEmpty"/> but warmed toward the amber it lights up in,
    /// so an idle proximity/shift segment reads as "this lamp is off" rather than as a gray dot.</summary>
    public static readonly Brush TrackEmptyWarm = Frozen(0x39, 0x2E, 0x1C);

    public static readonly Color PositiveColor = Color.FromRgb(0x3D, 0xDC, 0x7A);
    public static readonly Color NegativeColor = Color.FromRgb(0xFF, 0x5A, 0x5A);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
