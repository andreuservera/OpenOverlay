using System.Globalization;
using System.Text.RegularExpressions;

namespace IRacingOverlay.App.Layouts;

/// <summary>Layout names are unique, ignoring case. A name that is taken gets the first free
/// " (n)" suffix from 2 up, so "Race" becomes "Race (2)", and duplicating "Race (2)" gives
/// "Race (3)" rather than "Race (2) (2)".</summary>
public static partial class LayoutNaming
{
    public const string DefaultName = "Layout";

    public static string Unique(string desired, IEnumerable<string> taken)
    {
        var name = string.IsNullOrWhiteSpace(desired) ? DefaultName : desired.Trim();
        var names = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name))
        {
            return name;
        }

        var stem = Suffix().Replace(name, "");
        for (var n = 2; ; n++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({n})");
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    [GeneratedRegex(@"\s\(\d+\)$")]
    private static partial Regex Suffix();
}
