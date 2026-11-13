using IRacingOverlay.Coaching.Corners;
using IRacingOverlay.Coaching.Reports;

namespace IRacingOverlay.Coaching.History;

/// <summary>One saved session at a track, as history sees it.</summary>
/// <summary>One drive's report, and the other reports of the same drive (saved live and from its .ibt).</summary>
public sealed record SavedDrive(SessionReport Kept, IReadOnlyList<SessionReport> Copies);

public sealed record HistorySession(string ReportId, DateTime StartedUtc, double? BestLapTime, double? TheoreticalBestLapTime, int CleanLaps, string? SessionType);

/// <summary>One corner in one session.</summary>
/// <param name="BestTime">The session's best pass, corner plus the straight after it, seconds.</param>
/// <param name="MedianLoss">What a normal lap lost here in that session, seconds.</param>
/// <param name="MainFinding">The finding that came up most often here; null when none did.</param>
/// <param name="MainFindingShare">On what share of the compared passes it came up, 0–1.</param>
public sealed record CornerHistoryPoint(
    string ReportId,
    DateTime StartedUtc,
    double? BestTime,
    double? MedianLoss,
    int CleanPasses,
    string? MainFinding,
    double MainFindingShare);

/// <summary>A corner across every saved session at a track with one car, oldest session first.</summary>
public sealed record CornerHistory
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int ApexM { get; init; }

    /// <summary>The corner's type in the newest session (a <see cref="CornerType"/> name).</summary>
    public string? Type { get; init; }

    /// <summary>The sessions this corner was measured in, oldest first.</summary>
    public List<CornerHistoryPoint> Points { get; init; } = [];

    /// <summary>The quickest pass ever, seconds, and when.</summary>
    public double? BestEver { get; init; }

    public DateTime? BestEverUtc { get; init; }

    /// <summary>What a normal lap loses here now: the median of the last
    /// <see cref="TrackHistory.RecentSessions"/> sessions' normal loss, seconds.</summary>
    public double? RecentLoss { get; init; }

    /// <summary>The same over the first sessions: with <see cref="RecentLoss"/>, whether it is
    /// shrinking. Null with fewer than two sessions.</summary>
    public double? EarlyLoss { get; init; }

    /// <summary>The mistake that comes back most in this corner over recent sessions; null when none does.</summary>
    public FindingTally? Habit { get; init; }

    /// <summary>On what share of compared passes <see cref="Habit"/> came up in the first and in the
    /// newest session it was looked for (0–1): whether the habit is going away.</summary>
    public double? HabitShareFirst { get; init; }

    public double? HabitShareLast { get; init; }
}

/// <summary>
/// Every saved session at one track with one car read together, so the Coaching page can show
/// progress across sessions: per corner, the best pass ever, what a normal lap loses now against
/// at first, and whether a recurring mistake is going away. Corners are matched by where their apex
/// is (within <see cref="CornerMapBuilder.ClusterRadiusMetres"/>), so a relearned map still lines up.
/// </summary>
public sealed record TrackHistory
{
    /// <summary>How many of the newest (and oldest) sessions "recent" (and "early") are taken over.</summary>
    public const int RecentSessions = 3;

    public required string TrackKey { get; init; }

    public string? TrackName { get; init; }

    public required string Car { get; init; }

    /// <summary>Oldest first.</summary>
    public List<HistorySession> Sessions { get; init; } = [];

    /// <summary>The newest session's corners, in lap order.</summary>
    public List<CornerHistory> Corners { get; init; } = [];

    public double? BestLapEver => Sessions.Select(s => s.BestLapTime).Where(t => t is not null).Min();

    /// <summary>The history of the track and car <paramref name="reports"/> holds the most of,
    /// or of the given track and car; null when no report with corners matches.</summary>
    /// <summary>
    /// The same drive saved twice, live and again from its .ibt, counts once. Two reports are the same
    /// drive when they share laps: at least two lap times (one when a report has a single lap) within
    /// <see cref="SameLapSeconds"/>. The one with more clean laps is kept, the .ibt on a tie:
    /// it is read from the whole recording.
    /// </summary>
    /// <summary>Two laps this close are the same lap timed twice, seconds.</summary>
    public const double SameLapSeconds = 0.02;

    internal static List<SessionReport> OneReportPerDrive(List<SessionReport> reports) =>
        Drives(reports).Select(d => d.Kept).OrderBy(r => r.StartedUtc).ToList();

    /// <summary>The reports grouped by drive (see <see cref="OneReportPerDrive"/>), in the order the
    /// kept report first appears: the report to show for each drive and the other copies of it.</summary>
    public static IReadOnlyList<SavedDrive> Drives(IEnumerable<SessionReport> reports)
    {
        // Lap times to the hundredth are a fingerprint of a drive: the live coach and the .ibt time
        // the same laps. Start times alone aren't (an .ibt starts when the car leaves the pits, a
        // live report when coaching is switched on, and either may hold an out lap of minutes).
        static bool SameDrive(SessionReport a, SessionReport b)
        {
            if (a.Source == b.Source || a.TrackKey != b.TrackKey || a.CarName != b.CarName ||
                Math.Abs((a.StartedUtc - b.StartedUtc).TotalHours) > 3)
            {
                return false;
            }

            var times = b.Laps.Where(l => l.Time > 20).Select(l => l.Time).ToList();
            var shared = a.Laps.Count(l => l.Time > 20 && times.Any(t => Math.Abs(t - l.Time) < SameLapSeconds));
            var smaller = Math.Min(a.Laps.Count(l => l.Time > 20), times.Count);
            return smaller > 0 && shared >= Math.Min(2, smaller);
        }

        static int Rank(SessionReport r) => r.CleanLapCount * 2 + (r.Source == "ibt" ? 1 : 0);

        var drives = new List<(SessionReport Kept, List<SessionReport> Copies)>();
        foreach (var report in reports)
        {
            var twin = drives.FindIndex(d => d.Copies.Append(d.Kept).Any(k => SameDrive(k, report)));
            if (twin < 0)
            {
                drives.Add((report, []));
            }
            else if (Rank(report) > Rank(drives[twin].Kept))
            {
                drives[twin].Copies.Add(drives[twin].Kept);
                drives[twin] = (report, drives[twin].Copies);
            }
            else
            {
                drives[twin].Copies.Add(report);
            }
        }

        return drives.Select(d => new SavedDrive(d.Kept, d.Copies)).ToList();
    }

    public static TrackHistory? Build(IEnumerable<SessionReport> reports, string trackKey, string car)
    {
        var mine = reports
            .Where(r => r.TrackKey == trackKey && r.CarName == car && r.Corners.Count > 0 && r.CleanLapCount > 0)
            .OrderBy(r => r.StartedUtc)
            .ToList();
        mine = OneReportPerDrive(mine);
        if (mine.Count == 0)
        {
            return null;
        }

        var newest = mine[^1];
        var corners = new List<CornerHistory>();
        foreach (var c in newest.Corners)
        {
            var points = new List<(CornerHistoryPoint Point, CornerReport Corner)>();
            foreach (var r in mine)
            {
                var match = r.Corners.Where(o => Math.Abs(o.ApexM - c.ApexM) <= CornerMapBuilder.ClusterRadiusMetres).MinBy(o => Math.Abs(o.ApexM - c.ApexM));
                if (match is null || match.CleanPasses == 0)
                {
                    continue;
                }

                var top = match.Findings.FirstOrDefault();
                points.Add((new CornerHistoryPoint(r.Id, r.StartedUtc, match.BestTime, match.MedianLoss, match.CleanPasses,
                    top?.Kind, top is { OutOf: > 0 } ? (double)top.Laps / top.OutOf : 0), match));
            }

            var best = points.Where(p => p.Point.BestTime is not null).MinBy(p => p.Point.BestTime);
            var losses = points.Where(p => p.Point.MedianLoss is not null).Select(p => p.Point.MedianLoss!.Value).ToList();
            var recent = points.TakeLast(RecentSessions).ToList();

            // The habit: the finding with the most laps over the recent sessions.
            var habit = recent.SelectMany(p => p.Corner.Findings)
                .GroupBy(f => f.Kind)
                .Select(g => (Kind: g.Key, Laps: g.Sum(f => f.Laps), Latest: g.Last()))
                .Where(g => g.Laps >= 2)
                .OrderByDescending(g => g.Laps)
                .Select(g => g.Latest)
                .FirstOrDefault();
            double? Share(CornerReport r) => habit is null ? null
                : r.Findings.FirstOrDefault(f => f.Kind == habit.Kind) is { OutOf: > 0 } f ? (double)f.Laps / f.OutOf
                : r.ComparedPasses > 0 ? 0 : null;
            var shares = points.Select(p => Share(p.Corner)).Where(s => s is not null).ToList();

            corners.Add(new CornerHistory
            {
                Id = c.Id,
                Name = c.Name,
                ApexM = c.ApexM,
                Type = c.Type,
                Points = points.Select(p => p.Point).ToList(),
                BestEver = best.Point?.BestTime,
                BestEverUtc = best.Point?.StartedUtc,
                RecentLoss = Median(losses.TakeLast(RecentSessions)),
                EarlyLoss = losses.Count >= 2 ? Median(losses.Take(Math.Min(RecentSessions, losses.Count - 1))) : null,
                Habit = habit,
                HabitShareFirst = shares.Count >= 2 ? shares[0] : null,
                HabitShareLast = shares.Count >= 2 ? shares[^1] : null,
            });
        }

        return new TrackHistory
        {
            TrackKey = trackKey,
            TrackName = newest.TrackName,
            Car = car,
            Sessions = mine.Select(r => new HistorySession(r.Id, r.StartedUtc, r.BestLapTime, r.TheoreticalBestLapTime, r.CleanLapCount, r.SessionType)).ToList(),
            Corners = corners,
        };
    }

    private static double? Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        return v.Count == 0 ? null : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}
