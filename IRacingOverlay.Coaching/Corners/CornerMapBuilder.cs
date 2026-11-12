using IRacingOverlay.Coaching.Laps;

namespace IRacingOverlay.Coaching.Corners;

/// <summary>
/// Builds a track's corner map from several clean laps rather than one, so a corner that only some
/// laps happen to split, or a small dip only some laps show, doesn't change the map.
///
/// 1. Corner candidates are found on every lap (<see cref="CornerDetector"/>, before merging).
/// 2. Candidates from all laps are grouped by where their slowest point is: within
///    <see cref="ClusterRadiusMetres"/> of a group's median, they are the same corner.
/// 3. A group is kept only if it shows up in at least <see cref="QuorumShare"/> of the laps.
/// 4. Each kept corner takes the median start, apex and end of its group.
/// 5. Sub-corners are merged by the minimum apex gap rule and the result is numbered T1…Tn.
/// </summary>
public static class CornerMapBuilder
{
    /// <summary>Candidates whose slowest points are this close are the same corner, metres.</summary>
    public const int ClusterRadiusMetres = 50;

    /// <summary>Share of laps a corner must appear in to be kept (3 of 5). A lift the driver makes on
    /// some laps and not others stays out, so the map doesn't depend on which laps it was built from.</summary>
    public const double QuorumShare = 0.6;

    public static CornerMap Build(IReadOnlyList<LapTrace> laps, CornerDetectionOptions? options = null)
    {
        if (laps.Count == 0)
        {
            throw new ArgumentException("At least one lap is needed.", nameof(laps));
        }

        var o = options ?? new CornerDetectionOptions();
        var length = laps.Min(l => l.Length);

        var groups = new List<List<CornerCandidate>>();
        foreach (var candidate in laps.SelectMany(l => CornerDetector.DetectRaw(l, o)).OrderBy(c => c.ApexM))
        {
            var group = groups.FirstOrDefault(g => Math.Abs(Median(g.Select(c => c.ApexM)) - candidate.ApexM) <= ClusterRadiusMetres);
            if (group is null)
            {
                groups.Add([candidate]);
            }
            else
            {
                group.Add(candidate);
            }
        }

        var quorum = (int)Math.Ceiling(QuorumShare * laps.Count);
        var agreed = groups
            .Where(g => g.Count >= quorum)
            .Select(g => new CornerCandidate(
                Median(g.Select(c => c.StartM)),
                Median(g.Select(c => c.ApexM)),
                Median(g.Select(c => c.EndM)),
                (float)g.Select(c => (double)c.MinSpeed).OrderBy(v => v).ElementAt(g.Count / 2)))
            .Where(c => c.EndM < length)
            .ToList();

        return new CornerMap(length, CornerDetector.Name(CornerDetector.Merge(agreed, o)), laps.Count);
    }

    private static int Median(IEnumerable<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }
}
