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
    public static string Advice(string kind, double amount, bool isRamp, string? english, bool noBraking = false)
    {
        var text = MissingAmount(kind, amount, isRamp, english) ? null : TryT(Key("advice", kind, isRamp, noBraking));
        return text is null ? SessionSummaryText.Capital(english ?? kind) : string.Format(Inv, text, Whole(amount));
    }

    public static string Advice(PassIssue i) => Advice(i.Kind, i.Amount, i.IsRamp, null, i.NoBraking);

    /// <summary>What went wrong, as a sentence with how often and what it cost.</summary>
    public static string Problem(string kind, double amount, bool isRamp, int laps, int outOf, double loss, string? english, bool noBraking = false)
    {
        var text = MissingAmount(kind, amount, isRamp, english) ? null : TryT(Key("problem", kind, isRamp, noBraking));
        return text is null ? english ?? kind : string.Format(Inv, text, Whole(amount), laps, outOf, Seconds(loss));
    }

    // Older reports didn't save a mistake's size: say it with their saved English rather than "0 km/h".
    private static bool MissingAmount(string kind, double amount, bool isRamp, string? english) =>
        english is not null && amount <= 0 && !(kind == "InitialBrakeTooSoft" && isRamp);

    private static string Key(string what, string kind, bool isRamp, bool noBraking) => kind switch
    {
        "InitialBrakeTooSoft" when isRamp => $"coaching.{what}.{kind}.ramp",
        "Coasting" when noBraking => $"coaching.{what}.{kind}.noBraking",
        _ => $"coaching.{what}.{kind}",
    };

    // How CornerTypes.Label reads in English, as saved in older reports' kinds ("slow right · lift").
    private static readonly Dictionary<string, string> SavedTypes = new(StringComparer.Ordinal)
    {
        ["flat"] = "Flat",
        ["lift"] = "Lift",
        ["fast braking"] = "BrakingFast",
        ["slow"] = "Slow",
        ["complex"] = "Complex",
    };

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

    /// <summary>"slow right · lift", "left hairpin · slow"…: how the corner looks (the best pass's
    /// speed and which way it turns) and what decides its time (its type).</summary>
    public static string? CornerKind(CornerReport c)
    {
        // Older reports saved neither the direction nor the type, only English words: word it from those.
        var parts = c.Kind?.Split(" · ") ?? [];
        var type = c.Type ?? (parts.Length > 1 && SavedTypes.TryGetValue(parts[^1], out var savedType) ? savedType : null);
        var typeText = type is null ? null : TryT($"coaching.type.{type}") ?? type;
        var look = Look(c, parts.Length > 0 ? parts[0] : null);
        return look is null && typeText is null ? null : Format.Join(look, typeText);
    }

    private static string? Look(CornerReport c, string? saved)
    {
        if (c.Best is not { } best)
        {
            return null;
        }

        if (c.Direction == 0 && saved is not null && SavedKinds.TryGetValue(saved, out var key))
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

        if (WhereTheTimeGoes(r) is { } where)
        {
            lines.Add(where);
        }

        return lines;
    }

    /// <summary>As <see cref="SessionSummaryText.WhereTheTimeGoes"/>, in the app's language.</summary>
    public static string? WhereTheTimeGoes(SessionReport r)
    {
        var totals = r.Corners.SelectMany(c => c.LossByPhase)
            .GroupBy(PhaseKey)
            .Select(g => (Key: g.Key, Seconds: g.Sum(p => Math.Max(0, p.Seconds))))
            .Where(x => x.Seconds >= 0.01)
            .OrderByDescending(x => x.Seconds)
            .ToList();
        if (totals.Count == 0)
        {
            return null;
        }

        return totals.Count > 1
            ? F("coaching.summary.whereTwo", T($"coaching.where.{totals[0].Key}"), Seconds(totals[0].Seconds), T($"coaching.where.{totals[1].Key}"), Seconds(totals[1].Seconds))
            : F("coaching.summary.whereOne", T($"coaching.where.{totals[0].Key}"), Seconds(totals[0].Seconds));
    }

    /// <summary>A phase's key (Approach, Braking, Rotation, Traction, Straight), also for older reports that only saved the English.</summary>
    public static string PhaseKey(PhaseLossRow p) => p.Key ?? p.Phase switch
    {
        "Approach" => "Approach",
        "Braking" => "Braking",
        "Turn-in to throttle" => "Rotation",
        "Traction" => "Traction",
        _ => "Straight",
    };

    /// <summary>A phase's name: "Braking", "Turn-in to throttle".</summary>
    public static string Phase(PhaseLossRow p) => TryT($"coaching.phase.{PhaseKey(p)}") ?? p.Phase;

    /// <summary>What decides a corner's time, as a phrase ("a higher minimum speed was quicker");
    /// the saved English for a report without the data.</summary>
    public static string? DecidedBy(CornerReport c) =>
        c.DecidedByMeasure is { } m && c.DecidedByBetter is { } b
            ? TryT($"coaching.decided.{m}.{b}") ?? c.DecidedBy
            : c.DecidedBy;

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
