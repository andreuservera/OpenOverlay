namespace IRacingOverlay.App.Overlay;

/// <summary>The only sizes a widget can ever be. Free-form resizing is deliberately not offered:
/// arbitrary sizes let a container end up smaller than its content, which is what used to clip text
/// and break layouts.</summary>
public enum ScaleLevel
{
    XS,
    S,
    M,
    L,
    XL,
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

    private static readonly ScaleLevel[] Ordered =
        [ScaleLevel.XS, ScaleLevel.S, ScaleLevel.M, ScaleLevel.L, ScaleLevel.XL];

    public static double FactorOf(ScaleLevel level) => level switch
    {
        // 0.85 is the floor because it keeps the smallest type in the scale (11px) above 9px.
        ScaleLevel.XS => 0.85,
        ScaleLevel.S => 1.00,
        ScaleLevel.L => 1.35,
        ScaleLevel.XL => 1.60,
        _ => 1.15,
    };

    public static string LabelOf(ScaleLevel level) => level.ToString();

    public static bool IsSmallest(ScaleLevel level) => level == Ordered[0];

    public static bool IsLargest(ScaleLevel level) => level == Ordered[^1];

    /// <summary>Next level up, or the same level when already at XL — pressing "+" at the ceiling is
    /// a no-op rather than an error or a wrap-around to XS.</summary>
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
