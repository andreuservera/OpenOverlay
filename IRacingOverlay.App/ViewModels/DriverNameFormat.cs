using System.Globalization;
using System.Text;

namespace IRacingOverlay.App.ViewModels;

/// <summary>How a driver table writes each driver's name. Persisted by name in layouts and by
/// value in the settings file, so new formats go at the end.</summary>
public enum DriverNameFormat
{
    /// <summary>The whole name when the column has room, shortening step by step when it doesn't.</summary>
    Auto,
    /// <summary>Always the whole name, trimmed with an ellipsis if it overflows.</summary>
    Full,
    /// <summary>"C. Alvarez": first initial and surname, the same shape on every row.</summary>
    InitialSurname,
    /// <summary>"Alvarez".</summary>
    Surname,
    /// <summary>"ALV": three letters of the surname, as TV graphics write it.</summary>
    Code,
}

/// <summary>
/// Turns iRacing's single name string into the text a driver table shows. iRacing gives no first
/// name / surname split, so the last word is taken as the surname: right for most names, the second
/// surname for Spanish ones ("Carlos Carrasco Alvarez" → "Alvarez"), which no rule can tell apart.
/// </summary>
public static class DriverNameFormatter
{
    /// <summary>
    /// The texts to try, best first; the cell shows the first that fits its width, and the last one
    /// (trimmed with an ellipsis) when none does. Only <see cref="DriverNameFormat.Auto"/> offers
    /// more than one: every other format is a choice the user made, kept even when it overflows.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? raw, DriverNameFormat format, bool uppercaseSurname)
    {
        var parts = (raw ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return [string.Empty];
        }

        var surname = uppercaseSurname ? parts[^1].ToUpperInvariant() : parts[^1];
        var given = parts[..^1];
        var full = string.Join(' ', given.Append(surname));

        if (format == DriverNameFormat.Code)
        {
            return [Code(parts[^1])];
        }

        // A single word has nothing to drop or abbreviate.
        if (given.Length == 0)
        {
            return [full];
        }

        var initialSurname = $"{given[0][0]}. {surname}";
        return format switch
        {
            DriverNameFormat.Full => [full],
            DriverNameFormat.InitialSurname => [initialSurname],
            DriverNameFormat.Surname => [surname],
            _ => Distinct(
                full,
                // "M. G. Lopez": every given name and particle down to its initial.
                string.Concat(given.Select(p => $"{p[0]}. ")) + surname,
                initialSurname,
                surname),
        };
    }

    /// <summary>The first three letters of the surname, capitalised; digits iRacing appends to
    /// repeated names ("Smith2") and punctuation don't count.</summary>
    private static string Code(string surname)
    {
        var letters = new StringBuilder(3);
        foreach (var c in surname)
        {
            if (char.IsLetter(c))
            {
                letters.Append(char.ToUpperInvariant(c));
                if (letters.Length == 3)
                {
                    break;
                }
            }
        }

        return letters.Length > 0 ? letters.ToString() : surname.ToUpper(CultureInfo.InvariantCulture);
    }

    private static List<string> Distinct(params string[] candidates) => candidates.Distinct().ToList();
}
