using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Laps;
using IRacingOverlay.Coaching.Reports;

namespace IRacingOverlay.Coaching.References;

/// <summary>Why a corner is slower than the faster driver's lap.</summary>
public enum GapVerdict
{
    /// <summary>Within a tenth of the faster lap and of the session's best: nothing big to find.</summary>
    Level,

    /// <summary>Your best pass is close to the faster driver's, but a normal lap isn't: consistency.</summary>
    Consistency,

    /// <summary>Even your best pass is clearly slower: the faster driver does something you don't yet.</summary>
    Technique,
}

/// <summary>One measurement where the faster driver's pass differs from your best one.</summary>
/// <param name="Measure">brakePoint, minSpeed, throttlePoint, exitSpeed.</param>
/// <param name="Amount">How much, in metres or km/h; positive means the faster driver is later
/// (brake, throttle) or quicker (speeds).</param>
/// <param name="SetupSensitive">Speeds can come from a different setup as much as from driving.</param>
public sealed record GapDifference(string Measure, double Amount, bool SetupSensitive);

/// <summary>One corner: your best and normal against the faster driver's pass.</summary>
public sealed record CornerGap(string CornerId, string Name, double? YourBest, double FasterTime, double? Gap, double? NormalLoss, GapVerdict Verdict, IReadOnlyList<GapDifference> Differences);

/// <summary>
/// A session against a faster driver's lap (imported from their .ibt or an exported lap file),
/// corner by corner, so "I wasn't consistent" (your best is as quick, a normal lap isn't) reads
/// apart from "I don't know this corner yet" (even your best is slower). The faster lap is measured
/// on the session's own corners. Its setup may differ from yours, so differences are worded
/// cautiously: brake and throttle points are driving, speeds may be the car.
/// </summary>
public static class FasterLapComparison
{
    /// <summary>A gap or normal loss under this is noise, seconds.</summary>
    public const double MinGapSeconds = 0.1;

    /// <summary>The faster lap's track may be this much longer or shorter, metres.</summary>
    public const int MaxLengthDifferenceMetres = 25;

    /// <summary>Smallest differences worth saying: metres for points, km/h for speeds.</summary>
    public const double MinPointMetres = 5, MinSpeedKmh = 3;

    /// <summary>The faster lap's track length matches the session's (same layout).</summary>
    public static bool SameLayout(SessionReport r, LapTrace faster) => Math.Abs(faster.Length - r.TrackLengthMetres) <= MaxLengthDifferenceMetres;

    public static IReadOnlyList<CornerGap> Compare(SessionReport r, ReferenceLap faster)
    {
        if (r.Corners.Count == 0 || !SameLayout(r, faster.Trace))
        {
            return [];
        }

        var corners = r.Corners
            .Where(c => c.EndM < faster.Trace.Length)
            .Select(c => new Corner(c.Id, c.StartM, c.ApexM, c.EndM) { Label = c.Name })
            .ToList();
        var map = new CornerMap(r.TrackLengthMetres, corners);
        var passes = CornerAnalyzer.Analyze(faster.Trace, map, 0, LapFlaw.None).ToDictionary(p => p.Corner.Name, StringComparer.Ordinal);

        var result = new List<CornerGap>();
        foreach (var c in r.Corners)
        {
            if (!passes.TryGetValue(c.Id, out var f))
            {
                continue;
            }

            double? gap = c.BestTime is { } b ? b - f.ScoreTime : null;
            var verdict = gap is { } g && g >= MinGapSeconds && g >= (c.MedianLoss ?? 0) ? GapVerdict.Technique
                : (c.MedianLoss ?? 0) >= MinGapSeconds ? GapVerdict.Consistency
                : GapVerdict.Level;
            result.Add(new CornerGap(c.Id, c.Name, c.BestTime, f.ScoreTime, gap, c.MedianLoss, verdict, Differences(c.Best, f)));
        }

        return result;
    }

    private static IReadOnlyList<GapDifference> Differences(PassRow? mine, CornerPass f)
    {
        if (mine is null)
        {
            return [];
        }

        var d = new List<GapDifference>();
        if (mine.BrakePointM is { } mb && f.BrakePointM is { } fb && Math.Abs(fb - mb) >= MinPointMetres)
        {
            d.Add(new GapDifference("brakePoint", fb - mb, false));
        }

        var fMin = f.MinSpeed * 3.6;
        if (Math.Abs(fMin - mine.MinSpeedKmh) >= MinSpeedKmh)
        {
            d.Add(new GapDifference("minSpeed", fMin - mine.MinSpeedKmh, true));
        }

        if (mine.ThrottleM is { } mt && f.ThrottlePickupM is { } ft && Math.Abs(mt - ft) >= 2 * MinPointMetres)
        {
            // Positive: the faster driver is back on the throttle earlier.
            d.Add(new GapDifference("throttlePoint", mt - ft, false));
        }

        var fExit = f.ExitSpeedPlus100 * 3.6;
        if (Math.Abs(fExit - mine.ExitKmh) >= MinSpeedKmh)
        {
            d.Add(new GapDifference("exitSpeed", fExit - mine.ExitKmh, true));
        }

        return d;
    }
}
