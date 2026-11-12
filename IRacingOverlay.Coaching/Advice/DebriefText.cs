using System.Globalization;
using IRacingOverlay.Coaching.Corners;

namespace IRacingOverlay.Coaching.Advice;

/// <summary>
/// Puts a <see cref="LapDebrief"/> into words: a full written version, and a short spoken one that
/// gives only the one thing to fix in each of the worst corners. English for now; every phrase is in
/// this class so it can move to the app's localisation later.
/// </summary>
public static class DebriefText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Arriving this much slower than the best pass is worth pointing out, km/h.</summary>
    private const double InheritedEntryKmh = 3;

    /// <summary>One instruction for a finding, phrased as what to do next lap.</summary>
    public static string Advice(CornerFinding f) => f.Kind switch
    {
        FindingKind.BrakedTooEarly => $"brake {M(f.Amount)} later",
        FindingKind.BrakedTooLate => $"brake {M(f.Amount)} earlier, you overshot it",
        FindingKind.InitialBrakeTooSoft when f.IsRamp => "hit the brake harder at first, it took too long to build",
        FindingKind.InitialBrakeTooSoft => $"brake harder at first, {Pct(f.Amount)} less pressure than your best",
        FindingKind.InitialBrakeTooHard => $"too much brake, {Pct(f.Amount)} more pressure than your best",
        FindingKind.TooLittleTrailBrake => $"carry the brake {M(f.Amount)} further into the turn",
        FindingKind.LowMinimumSpeed => $"carry {Kmh(f.Amount)} more through the apex",
        FindingKind.LateThrottle => $"back on the throttle {M(f.Amount)} earlier",
        FindingKind.SlowExit => $"exit {Kmh(f.Amount)} slower than your best, get on the power cleaner",
        _ => f.Kind.ToString(),
    };

    /// <summary>The written debrief: lap, gap to best, then each corner with everything that differed.</summary>
    public static IReadOnlyList<string> Lines(LapDebrief d)
    {
        var lines = new List<string> { Headline(d, spoken: false) };
        foreach (var c in d.Corners)
        {
            var what = c.Findings.Count == 0
                ? "no single cause; compare the traces"
                : string.Join("; ", c.Findings.Select(Advice));
            var habit = c.RepeatCount > 0 ? $" (same as {c.RepeatCount} recent lap{(c.RepeatCount == 1 ? "" : "s")})" : "";
            var kind = c.Corner.Label is null && Describe(c.Best) is { } k ? $" ({k})" : "";
            var entry = c.EntrySlowerKmh >= InheritedEntryKmh ? $" Came in {Kmh(c.EntrySlowerKmh)} slower from the corner before." : "";
            lines.Add($"  {c.Corner.DisplayName}{kind}: lost {c.LossSeconds.ToString("0.00", Inv)} s. {Capital(what)}{habit}.{entry}");
        }

        if (d.TheoreticalBestLapTime is { } tb)
        {
            lines.Add($"  Your best corners add up to {LapTime(tb)}.");
        }

        return lines;
    }

    /// <summary>
    /// What is said after the lap: short enough to take in on the straight. The worst
    /// <paramref name="maxCorners"/> corners with a finding, each with only its first one.
    /// </summary>
    public static string Spoken(LapDebrief d, int maxCorners = 2)
    {
        var parts = new List<string> { Headline(d, spoken: true) };
        // Only corners with something to do; "you lost time somewhere" isn't worth hearing.
        foreach (var c in d.Corners.Where(c => c.Main is not null).Take(maxCorners))
        {
            var fix = Advice(c.Main!);
            parts.Add($"{SpokenCorner(c, again: c.RepeatCount >= 2)}, {Tenths(c.LossSeconds)}: {fix}.");
        }

        return string.Join(" ", parts);
    }

    private static string Headline(LapDebrief d, bool spoken)
    {
        var time = spoken ? SpokenLapTime(d.LapTime) : LapTime(d.LapTime);
        if (d.IsBestLap)
        {
            return spoken ? $"Best lap, {time}." : $"Lap {d.LapNumber}: {time}, best lap.";
        }

        return spoken
            ? $"{time}, {Tenths(d.DeltaToBestSeconds)} off your best."
            : $"Lap {d.LapNumber}: {time}, +{d.DeltaToBestSeconds.ToString("0.000", Inv)} s to best.";
    }

    private static string M(double metres) => $"{Math.Round(metres).ToString("0", Inv)} metres";

    private static string Kmh(double kmh) => $"{Math.Round(kmh).ToString("0", Inv)} km/h";

    private static string Pct(double points) => $"{Math.Round(points).ToString("0", Inv)}%";

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// How a corner is said. Its real name when one is set. Otherwise a bare "Turn 8" means nothing
    /// at speed, so it is described the way a driver thinks of it: "the last corner", "the first
    /// corner", or how slow it is and which way it goes ("Turn 8, the slow right").
    /// </summary>
    internal static string SpokenCorner(CornerAdvice c, bool again = false)
    {
        var a = again ? " again" : "";
        if (!string.IsNullOrWhiteSpace(c.Corner.Label))
        {
            return WithArticle(c.Corner.Label!.Trim()) + a;
        }

        var turn = c.Corner.Name.Length > 1 && c.Corner.Name[0] == 'T' && c.Corner.Name[1..].All(char.IsDigit)
            ? $"Turn {c.Corner.Name[1..]}"
            : c.Corner.Name;
        if (c.CornerCount > 1 && c.Index == c.CornerCount - 1)
        {
            return "The last corner" + a;
        }

        if (c.Index == 0)
        {
            return "Turn 1" + a;
        }

        return Describe(c.Best) is { } kind ? $"{turn}{a}, the {kind}" : turn + a;
    }

    // Names that are descriptions rather than proper names read better with "the" in front.
    private static readonly HashSet<string> GenericFirstWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hairpin", "Chicane", "First", "Second", "Third", "Last", "Final", "Snake", "Esses", "Kink",
        "Carousel", "Corkscrew", "Anti-Banked", "Bus", "Bridge", "Banking", "Bowl", "Dip",
    };

    internal static string WithArticle(string name)
    {
        // A numbered part ("Snake 2") is already a name.
        if (char.IsDigit(name[^1]))
        {
            return name;
        }

        var first = name.Split(' ', 2)[0];
        return GenericFirstWords.Contains(first) ? "The " + name.ToLowerInvariant() : name;
    }

    /// <summary>"hairpin", "slow right", "fast left"…; null when nothing is known.</summary>
    internal static string? Describe(CornerPass? best)
    {
        if (best is null)
        {
            return null;
        }

        var kmh = best.MinSpeed * 3.6;
        var way = best.Direction switch { > 0 => "left", < 0 => "right", _ => null };
        if (kmh < 75)
        {
            return way is null ? "hairpin" : $"{way} hairpin";
        }

        var pace = kmh < 115 ? "slow" : kmh < 165 ? null : "fast";
        return (pace, way) switch
        {
            (null, null) => null,
            (_, null) => $"{pace} corner",
            (null, _) => $"{way}-hander",
            _ => $"{pace} {way}",
        };
    }

    /// <summary>Time differences the way a race engineer says them.</summary>
    internal static string Tenths(double seconds)
    {
        if (seconds >= 1.0)
        {
            return $"{seconds.ToString("0.0", Inv)} seconds";
        }

        var tenths = (int)Math.Round(seconds * 10, MidpointRounding.AwayFromZero);
        return tenths switch
        {
            0 => "less than a tenth",
            1 => "a tenth",
            _ => $"{tenths} tenths",
        };
    }

    private static string LapTime(double s) => $"{(int)(s / 60)}:{(s % 60).ToString("00.000", Inv)}";

    private static string SpokenLapTime(double s)
    {
        var minutes = (int)(s / 60);
        var rest = (s % 60).ToString(minutes > 0 ? "00.0" : "0.0", Inv);
        return minutes > 0 ? $"{minutes} {rest}" : rest;
    }
}
