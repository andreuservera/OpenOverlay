using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// The tyre a car is on, as a timing table draws it: a ringed letter in the broadcast colours —
/// soft red, medium yellow, hard white, intermediate green, wet blue.
/// </summary>
public sealed record TireCompound(string Letter, string Color, string Name)
{
    private const string Neutral = "#C4CCD4";

    /// <summary>
    /// Names a CarIdxTireCompound index from the session's compound table. The table describes the
    /// player's car, so a car whose index isn't in it shows the number instead of a guessed name.
    /// Null when the sim reports no compound (-1) or this build has no compound table at all.
    /// </summary>
    public static TireCompound? Resolve(int index, IReadOnlyList<DriverTireEntry>? table)
    {
        if (index < 0 || table is not { Count: > 0 })
        {
            return null;
        }

        var entry = table.FirstOrDefault(t => t.TireIndex == index);
        return entry is null || string.IsNullOrWhiteSpace(entry.TireCompoundType)
            ? new TireCompound((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Neutral, $"Compound {index + 1}")
            : FromName(entry.TireCompoundType);
    }

    public static TireCompound FromName(string name)
    {
        var trimmed = name.Trim();
        var key = trimmed.ToLowerInvariant();
        return key switch
        {
            _ when key.Contains("inter") => new("I", "#34D399", trimmed),
            _ when key.Contains("wet") || key.Contains("rain") => new("W", "#3D8BFF", trimmed),
            _ when key.Contains("soft") => new("S", "#F04438", trimmed),
            _ when key.Contains("med") => new("M", "#FFD24D", trimmed),
            _ when key.Contains("hard") => new("H", "#F2F5F8", trimmed),
            _ when key.Contains("dry") => new("D", "#F2F5F8", trimmed),
            _ => new(trimmed.Length > 0 ? char.ToUpperInvariant(trimmed[0]).ToString() : "?", Neutral, trimmed),
        };
    }
}
