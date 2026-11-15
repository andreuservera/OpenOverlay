using IRacingOverlay.Coaching.Advice;

namespace IRacingOverlay.Coaching.Reports;

/// <summary>
/// Findings that pull opposite ways (brake earlier and brake later, a softer and a harder first
/// press). Over a handful of laps both can show up once each, which reads as advice contradicting
/// itself; it only says the corner isn't consistent yet.
/// </summary>
public static class FindingContradictions
{
    /// <summary>Laps the stronger of two opposite findings needs before it is shown alone.</summary>
    public const int MinLaps = 3;

    private static readonly (string A, string B)[] Opposites =
    [
        (nameof(FindingKind.BrakedTooEarly), nameof(FindingKind.BrakedTooLate)),
        (nameof(FindingKind.InitialBrakeTooSoft), nameof(FindingKind.InitialBrakeTooHard)),
    ];

    /// <summary>The findings without opposite pairs: of two opposite ones, the one with clearly more
    /// laps (at least <see cref="MinLaps"/> and more than the other) stays; otherwise neither.</summary>
    public static List<FindingTally> Without(IEnumerable<FindingTally> findings)
    {
        var list = findings.ToList();
        var drop = new HashSet<FindingTally>();
        foreach (var (a, b) in Opposites)
        {
            if (list.FirstOrDefault(f => f.Kind == a) is not { } fa || list.FirstOrDefault(f => f.Kind == b) is not { } fb)
            {
                continue;
            }

            var (strong, weak) = fa.Laps >= fb.Laps ? (fa, fb) : (fb, fa);
            drop.Add(weak);
            if (strong.Laps < MinLaps || strong.Laps == weak.Laps)
            {
                drop.Add(strong);
            }
        }

        return list.Where(f => !drop.Contains(f)).ToList();
    }
}
