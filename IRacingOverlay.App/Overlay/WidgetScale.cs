using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IRacingOverlay.App.Overlay;

/// <summary>
/// A widget's size, as a whole percentage of the standard size: 100 % is the standard, and the
/// range is <see cref="MinPercent"/> to <see cref="MaxPercent"/>. Every proportion in the widget
/// scales with it, so a widget looks the same at any size apart from how big it is; the container
/// follows the content, so no size can clip it.
///
/// The standard size sits above the raw design size (×<see cref="StandardFactor"/>): the panels are
/// drawn against a 1080p-era type scale that reads too small on the monitors these overlays run on.
/// The floor keeps the smallest type in the scale (12px) at 9px. Saved as the number; the names of
/// the old fixed sizes ("XL") still read, as the percentage each one was.
/// </summary>
[JsonConverter(typeof(WidgetScaleJsonConverter))]
public readonly record struct WidgetScale : IComparable<WidgetScale>
{
    public const int MinPercent = 65;
    public const int MaxPercent = 200;
    public const int DefaultPercent = 100;

    /// <summary>How far one press of the widget's own −/+ (or a Ctrl+wheel notch) moves it: 1 % at
    /// a time is too slow to reach across the range from the widget itself.</summary>
    public const int WidgetStep = 5;

    private const double StandardFactor = 1.15;

    // Stored as the distance from the standard size, so default(WidgetScale) is the standard size
    // rather than an out-of-range 0 %.
    private readonly int _offset;

    public WidgetScale(int percent) => _offset = Math.Clamp(percent, MinPercent, MaxPercent) - DefaultPercent;

    public int Percent => DefaultPercent + _offset;

    /// <summary>The factor the content is drawn at.</summary>
    public double Factor => StandardFactor * Percent / 100.0;

    /// <summary>"117 %".</summary>
    public string Label => string.Create(CultureInfo.InvariantCulture, $"{Percent} %");

    public bool IsSmallest => Percent <= MinPercent;

    public bool IsLargest => Percent >= MaxPercent;

    public static WidgetScale Default => default;

    // The old fixed sizes, as the percentages they were. Named sizes in older files read as these.
    public static WidgetScale XXS => new(65);
    public static WidgetScale XS => new(74);
    public static WidgetScale S => new(87);
    public static WidgetScale M => new(100);
    public static WidgetScale L => new(117);
    public static WidgetScale XL => new(139);
    public static WidgetScale XXL => new(165);
    public static WidgetScale XXXL => new(196);

    private static readonly Dictionary<string, WidgetScale> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["XXS"] = XXS, ["XS"] = XS, ["S"] = S, ["M"] = M, ["L"] = L, ["XL"] = XL, ["XXL"] = XXL, ["XXXL"] = XXXL, ["3XL"] = XXXL,
    };

    /// <summary>Bigger by <paramref name="step"/> percent, stopping at the largest.</summary>
    public WidgetScale Larger(int step = WidgetStep) => new(Percent + step);

    /// <summary>Smaller by <paramref name="step"/> percent, stopping at the smallest.</summary>
    public WidgetScale Smaller(int step = WidgetStep) => new(Percent - step);

    /// <summary>The size whose factor is closest to <paramref name="factor"/>, within the range.</summary>
    public static WidgetScale FromFactor(double factor) =>
        new((int)Math.Round(factor / StandardFactor * 100, MidpointRounding.AwayFromZero));

    /// <summary>A number ("117", "117 %") or an old size name ("XL"); null for anything else.</summary>
    public static WidgetScale? TryParse(string? text)
    {
        var trimmed = text?.Trim().TrimEnd('%').Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            return new WidgetScale(percent);
        }

        return Named.TryGetValue(trimmed, out var named) ? named : null;
    }

    /// <summary>As <see cref="TryParse"/>, falling back to the standard size for anything unrecognised
    /// (missing entry, hand-edited file).</summary>
    public static WidgetScale Parse(string? text) => TryParse(text) ?? Default;

    public int CompareTo(WidgetScale other) => Percent.CompareTo(other.Percent);

    public override string ToString() => Percent.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Writes the percentage as a number; reads a number, or an old size name.</summary>
internal sealed class WidgetScaleJsonConverter : JsonConverter<WidgetScale>
{
    public override WidgetScale Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => new WidgetScale((int)Math.Round(reader.GetDouble())),
            JsonTokenType.String => WidgetScale.TryParse(reader.GetString())
                ?? throw new JsonException($"\"{reader.GetString()}\" isn't a widget size."),
            _ => throw new JsonException("A widget size must be a number."),
        };

    public override void Write(Utf8JsonWriter writer, WidgetScale value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Percent);
}
