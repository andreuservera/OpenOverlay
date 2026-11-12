using System.Globalization;

namespace IRacingOverlay.Coaching.Advice;

/// <summary>
/// Puts a <see cref="LapDebrief"/> into words: a full written version, and a short spoken one that
/// gives only the one thing to fix in each of the worst corners. English for now; every phrase is in
/// this class so it can move to the app's localisation later.
/// </summary>
public static class DebriefText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>One instruction for a finding, phrased as what to do next lap.</summary>
    public static string Advice(CornerFinding f) => f.Kind switch
    {
        FindingKind.BrakedTooEarly => $"brake {M(f.Amount)} later",
        FindingKind.BrakedTooLate => $"brake {M(f.Amount)} earlier, you overshot it",
        FindingKind.InitialBrakeTooSoft when f.IsRamp => "hit the brake harder at first, it took too long to build",
        FindingKind.InitialBrakeTooSoft => $"brake harder at first, {Pct(f.Amount)} less pressure than your best",
        FindingKind.InitialBrakeTooHard => $"less brake pressure, {Pct(f.Amount)} more than your best",
        FindingKind.TooLittleTrailBrake => $"carry the brake {M(f.Amount)} further into the turn",
        FindingKind.LowMinimumSpeed => $"carry {Kmh(f.Amount)} more through the apex",
        FindingKind.LateThrottle => $"back on the throttle {M(f.Amount)} earlier",
        FindingKind.SlowExit => $"better exit, {Kmh(f.Amount)} slower onto the straight",
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
            lines.Add($"  {c.Corner.DisplayName}: -{c.LossSeconds.ToString("0.00", Inv)} s. {Capital(what)}{habit}.");
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
            var habit = c.RepeatCount >= 2 ? " Again." : "";
            parts.Add($"{SpokenCorner(c.Corner.DisplayName)}, {Tenths(c.LossSeconds)}: {fix}.{habit}");
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

    /// <summary>"T5" reads as "tee five" to a speech engine; say "Turn 5".</summary>
    private static string SpokenCorner(string name) =>
        name.Length > 1 && name[0] == 'T' && name[1..].All(char.IsDigit) ? $"Turn {name[1..]}" : name;

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
