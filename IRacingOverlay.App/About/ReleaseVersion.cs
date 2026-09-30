using System.Globalization;

namespace IRacingOverlay.App.About;

/// <summary>
/// A semantic version as CHANGELOG.md writes them: <c>X.Y.Z</c>, or <c>X.Y.Z-label</c> for a
/// preview. Build metadata — the "+commit" the SDK appends — is dropped on parsing, so two builds of
/// the same version compare equal.
/// </summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string Prerelease = "") : IComparable<ReleaseVersion>
{
    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>Accepts an optional leading "v" and trailing build metadata; anything that is not
    /// three numeric parts plus an optional pre-release label is rejected.</summary>
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = null!;
        var value = text?.Trim() ?? "";
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plus = value.IndexOf('+');
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var prerelease = "";
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = value[(dash + 1)..];
            value = value[..dash];
            if (!prerelease.Split('.').All(IsIdentifier))
            {
                return false;
            }
        }

        var parts = value.Split('.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch, prerelease);
        return true;
    }

    public static ReleaseVersion? ParseOrNull(string? text) => TryParse(text, out var version) ? version : null;

    /// <summary>SemVer precedence: numbers first, then a pre-release sorts before its release, and
    /// pre-release identifiers compare numerically where both are numbers.</summary>
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = Major != other.Major ? Major.CompareTo(other.Major)
            : Minor != other.Minor ? Minor.CompareTo(other.Minor)
            : Patch.CompareTo(other.Patch);
        if (core != 0)
        {
            return core;
        }

        if (IsPrerelease != other.IsPrerelease)
        {
            return IsPrerelease ? -1 : 1;
        }

        var mine = Prerelease.Split('.');
        var theirs = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            var result = CompareIdentifiers(mine[i], theirs[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    public override string ToString()
    {
        var core = string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        return IsPrerelease ? $"{core}-{Prerelease}" : core;
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    private static bool IsIdentifier(string identifier) =>
        identifier.Length > 0 && identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static int CompareIdentifiers(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric && rightNumeric)
        {
            // Compared as digit strings, so an identifier too long for an int still orders correctly.
            var a = left.TrimStart('0');
            var b = right.TrimStart('0');
            return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
        }

        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return string.CompareOrdinal(left, right);
    }
}
