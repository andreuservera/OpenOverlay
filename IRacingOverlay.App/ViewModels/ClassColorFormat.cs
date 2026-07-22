using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

internal static class ClassColorFormat
{
    /// <summary>
    /// iRacing supplies class colors as a hex integer string — confirmed live to actually come
    /// through with a "0x" prefix (e.g. "0x33ceff"), not just a bare hex string or "#"-prefixed one
    /// as the SDK docs' wording suggested. Stripping only "#" left the "0x" in place, which made
    /// every single class silently fail to parse and fall back to white — the real cause behind
    /// "class colors all look the same," not (or not only) a too-low tint opacity. Normalize to
    /// "#RRGGBB" regardless of which prefix (or none) shows up.
    /// </summary>
    public static string Normalize(string carClassColor)
    {
        if (string.IsNullOrWhiteSpace(carClassColor))
        {
            return "#FFFFFF";
        }

        var trimmed = carClassColor.Trim();
        trimmed = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? trimmed[2..]
            : trimmed.TrimStart('#');

        return int.TryParse(trimmed, NumberStyles.HexNumber, null, out var value)
            ? $"#{value & 0xFFFFFF:X6}"
            : "#FFFFFF";
    }
}
