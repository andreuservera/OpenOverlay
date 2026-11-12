using System.Globalization;
using System.Text.RegularExpressions;

namespace IRacingOverlay.Coaching.Reports;

/// <summary>
/// The session summary in words, for the Coaching page and the CLI. English, like the debrief
/// (<see cref="Advice.DebriefText"/>); every phrase is here so it can be translated later.
/// </summary>
public static partial class SessionSummaryText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The summary's opening lines: best lap, theoretical best, consistency, and where a normal lap loses most.</summary>
    public static IReadOnlyList<string> Headline(SessionReport r)
    {
        var lines = new List<string>();
        var clean = r.CleanLapCount;
        if (r.BestLapTime is { } best)
        {
            lines.Add($"Best lap {LapTime(best)} on lap {r.BestLapNumber}, {clean} clean lap{S(clean)} out of {r.Laps.Count}.");
            if (r.TheoreticalBestLapTime is { } tb)
            {
                var gain = best - tb;
                lines.Add(gain >= 0.0005
                    ? $"Your best corners add up to {LapTime(tb)}: {Seconds(gain)} quicker than your best lap."
                    : $"Your best lap already holds your best pass through almost every corner ({LapTime(tb)}).");
            }
        }
        else
        {
            lines.Add($"No clean lap out of {r.Laps.Count}.");
        }

        if (r.AverageGapToBest is { } gap && r.CleanLapSpread is { } spread)
        {
            lines.Add($"Clean laps were {Seconds(gap)} off your best on average, with a spread of {Seconds(spread)}.");
        }

        var costliest = r.Corners.Where(c => c.MedianLoss is > 0.0005).MaxBy(c => c.MedianLoss);
        if (costliest is not null)
        {
            lines.Add($"A normal lap loses the most at {costliest.Name}: {Seconds(costliest.MedianLoss!.Value)} against your best pass.");
        }

        return lines;
    }

    /// <summary>What went wrong, as a sentence: "You braked about 12 m early on 6 of 9 laps, losing about 0.12 s each time."</summary>
    public static string Problem(FindingTally f) =>
        $"You {Phrase(f.Kind, f.TypicalAmount, f.Advice)} on {f.Laps} of {f.OutOf} laps, losing about {Seconds(f.TypicalLoss)} each time.";

    /// <summary>The finding as something the driver did, past tense.</summary>
    internal static string Phrase(string kind, double amount, string advice) => kind switch
    {
        "BrakedTooEarly" => $"braked about {M(amount)} early",
        "BrakedTooLate" => $"braked about {M(amount)} too late and overshot",
        "InitialBrakeTooSoft" when advice.Contains("took too long", StringComparison.Ordinal) => "built the brake pressure too slowly",
        "InitialBrakeTooSoft" => $"hit the brake too softly ({Pct(amount)} less pressure)",
        "InitialBrakeTooHard" => $"braked too hard ({Pct(amount)} more pressure)",
        "TooLittleTrailBrake" => $"let go of the brake about {M(amount)} too soon after turning in",
        "LowMinimumSpeed" => $"were about {Kmh(amount)} slower at the apex",
        "LateThrottle" => $"got back on the throttle about {M(amount)} late",
        "SlowExit" => $"came out about {Kmh(amount)} slower",
        _ => Words(kind),
    };

    public static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string LapTime(double s) => s <= 0 ? "-" : $"{(int)(s / 60)}:{(s % 60).ToString("00.000", Inv)}";

    public static string Seconds(double s) => $"{s.ToString("0.00", Inv)} s";

    private static string S(int n) => n == 1 ? "" : "s";

    private static string M(double metres) => $"{Math.Round(metres).ToString("0", Inv)} m";

    private static string Kmh(double kmh) => $"{Math.Round(kmh).ToString("0", Inv)} km/h";

    private static string Pct(double points) => $"{Math.Round(points).ToString("0", Inv)}%";

    // A kind this version has no phrase for ("LiftTooEarly") still reads: "had lift too early".
    private static string Words(string kind) => "had " + CamelBreak().Replace(kind, " $1").Trim().ToLowerInvariant();

    [GeneratedRegex("([A-Z])")]
    private static partial Regex CamelBreak();
}
