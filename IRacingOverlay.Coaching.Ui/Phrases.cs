using System.Globalization;
using IRacingOverlay.Coaching.Reports;
using static IRacingOverlay.Coaching.Ui.CoachingText;

namespace IRacingOverlay.Coaching.Ui;

/// <summary>
/// A session report put into words in the app's language. Reports keep the findings as data (kind
/// and size), so they are worded here when shown; a kind the language files don't know (from a
/// newer coach) falls back to the English saved in the report.
/// </summary>
internal static class Phrases
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Seconds(double s) => $"{s.ToString("0.00", Inv)} s";

    public static string LapTime(double s) => SessionSummaryText.LapTime(s);

    public static string Whole(double v) => Math.Round(v).ToString("0", Inv);

    /// <summary>What to do about a finding: "Brake 12 m later".</summary>
    public static string Advice(string kind, double amount, bool isRamp, string? english)
    {
        var text = TryT(Key("advice", kind, isRamp));
        return text is null ? SessionSummaryText.Capital(english ?? kind) : string.Format(Inv, text, Whole(amount));
    }

    public static string Advice(PassIssue i) => Advice(i.Kind, i.Amount, i.IsRamp, null);

    /// <summary>What went wrong, as a sentence with how often and what it cost.</summary>
    public static string Problem(string kind, double amount, bool isRamp, int laps, int outOf, double loss, string? english)
    {
        var text = TryT(Key("problem", kind, isRamp));
        return text is null ? english ?? kind : string.Format(Inv, text, Whole(amount), laps, outOf, Seconds(loss));
    }

    private static string Key(string what, string kind, bool isRamp) =>
        kind == "InitialBrakeTooSoft" && isRamp ? $"coaching.{what}.{kind}.ramp" : $"coaching.{what}.{kind}";

    // What DebriefText.Describe says for each kind, as saved in older reports.
    private static readonly Dictionary<string, string> SavedKinds = new(StringComparer.Ordinal)
    {
        ["hairpin"] = "coaching.kind.hairpin",
        ["left hairpin"] = "coaching.kind.hairpinLeft",
        ["right hairpin"] = "coaching.kind.hairpinRight",
        ["slow corner"] = "coaching.kind.slow",
        ["fast corner"] = "coaching.kind.fast",
        ["left-hander"] = "coaching.kind.left",
        ["right-hander"] = "coaching.kind.right",
        ["slow left"] = "coaching.kind.slowLeft",
        ["slow right"] = "coaching.kind.slowRight",
        ["fast left"] = "coaching.kind.fastLeft",
        ["fast right"] = "coaching.kind.fastRight",
    };

    /// <summary>"slow right", "left hairpin"…, from the best pass's speed and which way it turns.</summary>
    public static string? CornerKind(CornerReport c)
    {
        if (c.Best is not { } best)
        {
            return null;
        }

        // Reports from before the direction was saved: their English words name the kind, so word it from those.
        if (c.Direction == 0 && c.Kind is { } saved && SavedKinds.TryGetValue(saved, out var key))
        {
            return T(key);
        }

        var way = c.Direction switch { > 0 => "Left", < 0 => "Right", _ => "" };
        var kmh = best.MinSpeedKmh;
        if (kmh < 75)
        {
            return T(way.Length == 0 ? "coaching.kind.hairpin" : $"coaching.kind.hairpin{way}");
        }

        var pace = kmh < 115 ? "slow" : kmh < 165 ? "" : "fast";
        return (pace, way) switch
        {
            ("", "") => null,
            (_, "") => T($"coaching.kind.{pace}"),
            ("", _) => T($"coaching.kind.{way.ToLowerInvariant()}"),
            _ => T($"coaching.kind.{pace}{way}"),
        };
    }

    /// <summary>The summary's opening lines, as <see cref="SessionSummaryText.Headline"/> but in the app's language.</summary>
    public static IReadOnlyList<string> Headline(SessionReport r)
    {
        var lines = new List<string>();
        if (r.BestLapTime is { } best)
        {
            lines.Add(F("coaching.summary.bestLap", LapTime(best), r.BestLapNumber, r.CleanLapCount, r.Laps.Count));
            if (r.TheoreticalBestLapTime is { } tb)
            {
                lines.Add(best - tb >= 0.0005
                    ? F("coaching.summary.theoretical", LapTime(tb), Seconds(best - tb))
                    : F("coaching.summary.theoreticalSame", LapTime(tb)));
            }
        }
        else
        {
            lines.Add(F("coaching.summary.noCleanLap", r.Laps.Count));
        }

        if (r.AverageGapToBest is { } gap && r.CleanLapSpread is { } spread)
        {
            lines.Add(F("coaching.summary.consistency", Seconds(gap), Seconds(spread)));
        }

        if (r.Corners.Where(c => c.MedianLoss is > 0.0005).MaxBy(c => c.MedianLoss) is { } costliest)
        {
            lines.Add(F("coaching.summary.costliest", costliest.Name, Seconds(costliest.MedianLoss!.Value)));
        }

        return lines;
    }

    /// <summary>The notes about the session as a whole (car damage, too few laps), worded from the report's data.</summary>
    public static IReadOnlyList<string> Notes(SessionReport r)
    {
        var notes = new List<string>();
        if (r.Corners.Count == 0)
        {
            notes.Add(T("coaching.note.noCorners"));
        }

        if (r.CarChangedAfterLap is { } changed)
        {
            notes.Add(F("coaching.note.carChanged", changed));
        }

        var clean = r.Laps.Count(l => l.Clean && l.Compared);
        if (r.Corners.Count > 0 && clean < 3)
        {
            notes.Add(clean == 1 ? T("coaching.note.oneCleanLap") : F("coaching.note.fewCleanLaps", clean));
        }

        return notes;
    }

    /// <summary>A lap or corner flaw ("OffTrack") in words; a list of them ("OffTrack, Shortcut") too.</summary>
    public static string Flaws(string? codes) =>
        string.IsNullOrWhiteSpace(codes)
            ? ""
            : string.Join(", ", codes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(c => TryT($"coaching.flaw.{c}") ?? c));
}
