namespace IRacingOverlay.App.Overlay;

/// <summary>The only sizes a widget can ever be. Free-form resizing is deliberately not offered:
/// arbitrary sizes let a container end up smaller than its content, which is what used to clip text
/// and break layouts. Declared smallest to largest: the Control Panel's size selector indexes by it.
/// Persisted by name, so the order can change without reinterpreting saved sizes.</summary>
public enum ScaleLevel
{
    XXS,
    XS,
    S,
    M,
    L,
    XL,
    XXL,
    XXXL,
}

/// <summary>
/// The shared scale ladder. Steps are geometric (each level is roughly 1.17x the previous one) so
/// every press of +/- feels like the same amount of change regardless of where you are on the
/// ladder. The whole ladder sits above the raw design size — even M renders at 1.15x — because the
/// panels are drawn against a 1080p-era type scale that reads too small on the monitors these
/// overlays actually run on.
/// </summary>
internal static class ScaleLevels
{
    public const ScaleLevel Default = ScaleLevel.M;

    private static readonly ScaleLevel[] Ordered = Enum.GetValues<ScaleLevel>();

    public static double FactorOf(ScaleLevel level) => level switch
    {
        // 0.75 is the floor because it keeps the smallest type in the scale (12px) at 9px.
        ScaleLevel.XXS => 0.75,
        ScaleLevel.XS => 0.85,
        ScaleLevel.S => 1.00,
        ScaleLevel.L => 1.35,
        ScaleLevel.XL => 1.60,
        ScaleLevel.XXL => 1.90,
        ScaleLevel.XXXL => 2.25,
        _ => 1.15,
    };

    public static string LabelOf(ScaleLevel level) => level == ScaleLevel.XXXL ? "3XL" : level.ToString();

    /// <summary>Every level's label, smallest first.</summary>
    public static string[] Labels => Ordered.Select(LabelOf).ToArray();

    public static bool IsSmallest(ScaleLevel level) => level == Ordered[0];

    public static bool IsLargest(ScaleLevel level) => level == Ordered[^1];

    /// <summary>Next level up, or the same level when already at the largest — pressing "+" at the
    /// ceiling is a no-op rather than an error or a wrap-around to the smallest.</summary>
    public static ScaleLevel Larger(ScaleLevel level) => Step(level, +1);

    public static ScaleLevel Smaller(ScaleLevel level) => Step(level, -1);

    private static ScaleLevel Step(ScaleLevel level, int direction)
    {
        var index = Array.IndexOf(Ordered, level);
        if (index < 0)
        {
            return Default;
        }

        return Ordered[Math.Clamp(index + direction, 0, Ordered.Length - 1)];
    }

    /// <summary>Parses a persisted level name, falling back to M for anything unrecognised (missing
    /// entry, hand-edited file, or a value written by an older build that stored raw multipliers).</summary>
    public static ScaleLevel Parse(string? name) =>
        Enum.TryParse<ScaleLevel>(name, ignoreCase: true, out var level) && Enum.IsDefined(level)
            ? level
            : Default;
}
