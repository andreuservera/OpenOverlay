namespace IRacingOverlay.Coaching.Corners;

/// <summary>
/// Picks the reference pass for each corner from every clean pass, whatever lap it came from: a lap
/// with an off at one corner still offers its other corners. Passes are judged on
/// <see cref="CornerPass.ScoreTime"/>, so a pass that gained in the corner but lost more on the
/// straight after it is not the best.
///
/// One freak pass (a timing glitch, a cut the checks missed) must never become the target: a pass
/// much faster than the driver's usual pace through that corner only counts once another pass comes
/// close to it.
/// </summary>
public static class BestPasses
{
    /// <summary>A pass is suspiciously fast when it beats the median pass by more than this share…</summary>
    public const double OutlierShare = 0.04;

    /// <summary>…or by more than this, seconds (whichever is smaller).</summary>
    public const double OutlierSeconds = 0.5;

    /// <summary>Another pass within this share of a suspicious one confirms it…</summary>
    public const double ConfirmShare = 0.01;

    /// <summary>…or within this, seconds (whichever is larger).</summary>
    public const double ConfirmSeconds = 0.1;

    /// <summary>The outlier rule needs at least this many clean passes to know the usual pace.</summary>
    public const int MinPassesForOutlierCheck = 3;

    /// <summary>The best clean, believable pass of each corner, by corner id.</summary>
    public static Dictionary<string, CornerPass> Select(IEnumerable<CornerPass> passes)
    {
        var best = new Dictionary<string, CornerPass>(StringComparer.Ordinal);
        foreach (var group in passes.Where(p => p.IsClean).GroupBy(p => p.Corner.Name, StringComparer.Ordinal))
        {
            if (Best(group.ToList()) is { } b)
            {
                best[group.Key] = b;
            }
        }

        return best;
    }

    /// <summary>The fastest believable pass among clean passes of one corner; null when none.</summary>
    public static CornerPass? Best(IReadOnlyList<CornerPass> passes)
    {
        if (passes.Count == 0)
        {
            return null;
        }

        var ordered = passes.OrderBy(p => p.ScoreTime).ToList();
        if (ordered.Count < MinPassesForOutlierCheck)
        {
            return ordered[0];
        }

        var median = ordered[ordered.Count / 2].ScoreTime;
        for (var i = 0; i < ordered.Count; i++)
        {
            var t = ordered[i].ScoreTime;
            var suspicious = median - t > Math.Min(OutlierShare * median, OutlierSeconds);
            if (!suspicious)
            {
                return ordered[i];
            }

            var tolerance = Math.Max(ConfirmShare * t, ConfirmSeconds);
            if (i + 1 < ordered.Count && ordered[i + 1].ScoreTime - t <= tolerance)
            {
                return ordered[i];
            }
        }

        return ordered[0];
    }

    /// <summary>
    /// The best passes chained into a lap: the best clean run from the line to the first corner,
    /// plus each corner's best <see cref="CornerPass.ScoreTime"/> and the best clean rest of the
    /// straight after it (<see cref="CornerPass.RestOfStraightTime"/>). Null until every corner of
    /// <paramref name="map"/> has a best pass and the run to the first corner has been clean once.
    /// Corners from different laps may not quite join up, so it is a target, not a promise.
    /// </summary>
    public static double? TheoreticalLapTime(
        CornerMap map,
        IReadOnlyDictionary<string, CornerPass> best,
        double? bestOutsideCornersTime,
        IReadOnlyDictionary<string, double> bestRestOfStraight)
    {
        if (bestOutsideCornersTime is not { } outside || map.Corners.Count == 0)
        {
            return null;
        }

        var sum = outside;
        foreach (var c in map.Corners)
        {
            if (!best.TryGetValue(c.Name, out var b))
            {
                return null;
            }

            if (!bestRestOfStraight.TryGetValue(c.Name, out var rest))
            {
                return null;
            }

            sum += b.ScoreTime + rest;
        }

        return sum;
    }

    /// <summary>The fastest clean rest-of-straight after each corner, by corner id. A straight
    /// after a cut doesn't count: the cut makes the car faster onto it.</summary>
    public static Dictionary<string, double> RestOfStraight(IEnumerable<CornerPass> passes)
    {
        var best = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var p in passes)
        {
            // After a cut the car is faster onto the straight; before an off into the next corner
            // the straight was likely gained by braking too late.
            if (!p.RestOfStraightClean || (p.Flaws & (CornerPassFlaw.Shortcut | CornerPassFlaw.OffTrackNext)) != 0)
            {
                continue;
            }

            if (!best.TryGetValue(p.Corner.Name, out var t) || p.RestOfStraightTime < t)
            {
                best[p.Corner.Name] = p.RestOfStraightTime;
            }
        }

        return best;
    }
}
