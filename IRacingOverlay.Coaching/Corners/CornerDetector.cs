using IRacingOverlay.Coaching.Laps;

namespace IRacingOverlay.Coaching.Corners;

/// <summary>Tuning for <see cref="CornerDetector"/>. Defaults suit road courses in road cars.</summary>
public sealed record CornerDetectionOptions
{
    /// <summary>A slowdown only counts as a corner if the track bends tighter than this radius near
    /// its slowest point (rules out lifting for traffic on a straight), metres.</summary>
    public double MaxCornerRadiusMetres { get; init; } = 400;

    /// <summary>Speed must fall and then rise again by at least this much for a dip to count…</summary>
    public float MinSpeedDropMetresPerSecond { get; init; } = 1.0f;

    /// <summary>…and by at least this share of the speed (3% = 6 km/h at 200 km/h).</summary>
    public float MinSpeedDropShare { get; init; } = 0.03f;

    /// <summary>How far either side of the slowest point to look for the bend, metres.</summary>
    public int ApexSearchMetres { get; init; } = 60;

    /// <summary>Width of the centred moving averages applied to curvature and speed, metres.</summary>
    public int SmoothingMetres { get; init; } = 20;

    /// <summary>Corners whose slowest points are closer than this are one corner (a chicane, Les
    /// Combes, the Bus Stop): a coach talks about them as one, and lap-to-lap noise decides whether the
    /// short burst of speed between them is detected at all, metres.</summary>
    public int MinApexGapMetres { get; init; } = 80;

    /// <summary>Once back on full throttle, the window waits at most this long for the track to
    /// straighten before it ends anyway, metres. It keeps a long, fast exit (Pouhon, Dunlop) out of
    /// the corner before it.</summary>
    public int ExitGraceMetres { get; init; } = 60;

    /// <summary>When the driver never got back to full throttle before a corner, its window starts
    /// at its braking, at most this far back over the lift before it, metres.</summary>
    public int MaxLiftBeforeBrakeMetres { get; init; } = 100;

    /// <summary>Brake above this is braking.</summary>
    public float BrakeOn { get; init; } = 0.05f;

    /// <summary>Throttle at or above this is "full throttle".</summary>
    public float FullThrottle { get; init; } = 0.95f;
}

/// <summary>
/// Finds the corners of a track from one clean lap. A corner, for coaching, is somewhere the driver
/// has to slow down for a bend, so detection starts from speed rather than track shape:
///
/// 1. Speed is smoothed and split into rises and falls with hysteresis: a fall only ends (a valley)
///    once speed has climbed back by <see cref="CornerDetectionOptions.MinSpeedDropMetresPerSecond"/>
///    and <see cref="CornerDetectionOptions.MinSpeedDropShare"/>, so noise and small corrections
///    don't split one corner in two, while a chicane's two apexes with no real acceleration between
///    them stay one corner.
/// 2. Each valley is the apex of a corner, kept only if the track really bends there (curvature from
///    yaw rate / speed, or lateral g / speed², tighter than <see cref="CornerDetectionOptions.MaxCornerRadiusMetres"/>)
///    and the driver braked or lifted for it. Flat-out kinks and fast sweepers are not coachable.
/// 3. The corner window starts where braking or lifting began after the previous speed peak and ends
///    where the driver is back to full throttle, never past the next speed peak. On tight tracks
///    (Tsukuba, Okayama) the driver is never flat between corners, and that speed peak is the boundary.
///
/// 4. Corners whose slowest points are closer than <see cref="CornerDetectionOptions.MinApexGapMetres"/>
///    are merged into one (chicanes, esses).
///
/// A corner that straddles the start/finish line is cut at the line; the grid has no wrap-around yet.
/// </summary>
public static class CornerDetector
{
    // Below this speed, yaw rate / speed blows up (pit lane, spins); treat as no curvature data.
    private const float MinSpeedForCurvature = 8f;

    /// <summary>The corners of one lap, sub-corners merged and named T1…Tn. For a map that stays the
    /// same from session to session, build it from several laps with <see cref="CornerMapBuilder"/>.</summary>
    public static CornerMap Detect(LapTrace lap, CornerDetectionOptions? options = null)
    {
        var o = options ?? new CornerDetectionOptions();
        return new CornerMap(lap.Length, Name(Merge(DetectRaw(lap, o), o)));
    }

    /// <summary>Every corner candidate of one lap, before sub-corners are merged.</summary>
    internal static List<CornerCandidate> DetectRaw(LapTrace lap, CornerDetectionOptions o)
    {
        var n = lap.Length;
        var curvature = Smooth(Curvature(lap), o.SmoothingMetres);
        var speed = Smooth(Array.ConvertAll(lap.Speed, v => (double)v), o.SmoothingMetres / 2);
        var threshold = 1.0 / o.MaxCornerRadiusMetres;

        // 1. peaks and valleys of speed, with hysteresis
        var peaks = new List<int>();
        var valleys = new List<int>();
        var rising = true;
        int hi = 0, lo = 0;
        for (var i = 1; i < n; i++)
        {
            if (rising)
            {
                if (speed[i] > speed[hi])
                {
                    hi = i;
                }
                else if (speed[hi] - speed[i] >= Drop(speed[hi], o))
                {
                    peaks.Add(hi);
                    rising = false;
                    lo = i;
                }
            }
            else
            {
                if (speed[i] < speed[lo])
                {
                    lo = i;
                }
                else if (speed[i] - speed[lo] >= Drop(speed[lo], o))
                {
                    valleys.Add(lo);
                    rising = true;
                    hi = i;
                }
            }
        }

        // 2 + 3. keep real corners and size their windows
        var corners = new List<CornerCandidate>();
        foreach (var valley in valleys)
        {
            var peakBefore = peaks.LastOrDefault(p => p < valley); // 0 when none
            var peakAfter = peaks.FirstOrDefault(p => p > valley);
            if (peakAfter == 0)
            {
                peakAfter = n - 1;
            }

            var apex = ArgMin(lap.Speed, peakBefore, peakAfter);
            if (!BendsNear(curvature, apex, o.ApexSearchMetres, threshold))
            {
                continue;
            }

            var start = peakBefore;
            while (start < apex && IsFlatOut(lap, start, o))
            {
                start++;
            }

            if (start == apex)
            {
                continue; // speed fell with the driver flat out: a hill, not a corner
            }

            start = Math.Max(start, EntryStart(lap, start, apex, o));

            // Out of the corner = back on full throttle with the track no longer bending (a chicane's
            // second half is still the corner), but never past the next speed peak.
            // A long fast bend after the exit is driven flat and isn't part of this corner, so stop
            // ExitGraceMetres after full throttle whatever the curvature.
            var end = apex;
            int? flatSince = null;
            while (end < peakAfter)
            {
                if (IsFlatOut(lap, end, o))
                {
                    flatSince ??= end;
                    if (curvature[end] < threshold || end - flatSince >= o.ExitGraceMetres)
                    {
                        break;
                    }
                }
                else
                {
                    flatSince = null;
                }

                end++;
            }

            corners.Add(new CornerCandidate(start, apex, end, lap.Speed[apex]));
        }

        return corners;
    }

    /// <summary>Joins consecutive candidates whose slowest points are within
    /// <see cref="CornerDetectionOptions.MinApexGapMetres"/>, and trims any overlap between neighbours.</summary>
    internal static List<CornerCandidate> Merge(IReadOnlyList<CornerCandidate> candidates, CornerDetectionOptions o)
    {
        var merged = new List<CornerCandidate>();
        var lastSubApex = int.MinValue;
        foreach (var candidate in candidates.OrderBy(x => x.ApexM))
        {
            var c = candidate;
            if (merged.Count > 0 && c.ApexM - lastSubApex < o.MinApexGapMetres)
            {
                var m = merged[^1];
                var slower = c.MinSpeed < m.MinSpeed ? c : m;
                merged[^1] = new CornerCandidate(Math.Min(m.StartM, c.StartM), slower.ApexM, Math.Max(m.EndM, c.EndM), slower.MinSpeed);
            }
            else
            {
                if (merged.Count > 0 && c.StartM < merged[^1].EndM)
                {
                    // Overlapping windows: split at the middle of the gap between the two apexes' windows.
                    var m = merged[^1];
                    var cut = Math.Clamp((m.EndM + c.StartM) / 2, m.ApexM, c.ApexM);
                    merged[^1] = m with { EndM = cut };
                    c = c with { StartM = cut };
                }

                merged.Add(c);
            }

            lastSubApex = c.ApexM;
        }

        return merged;
    }

    internal static List<Corner> Name(IEnumerable<CornerCandidate> candidates) =>
        candidates.Select((c, i) => new Corner($"T{i + 1}", c.StartM, c.ApexM, c.EndM)).ToList();

    private static double Drop(double speed, CornerDetectionOptions o) =>
        Math.Max(o.MinSpeedDropMetresPerSecond, o.MinSpeedDropShare * speed);

    private static bool BendsNear(double[] curvature, int at, int reach, double threshold)
    {
        var from = Math.Max(0, at - reach);
        var to = Math.Min(curvature.Length - 1, at + reach);
        for (var i = from; i <= to; i++)
        {
            if (curvature[i] >= threshold)
            {
                return true;
            }
        }

        return false;
    }

    internal static double[] Curvature(LapTrace lap)
    {
        var n = lap.Length;
        var useYaw = false;
        for (var i = 0; i < n && !useYaw; i++)
        {
            useYaw = lap.YawRate[i] != 0;
        }

        var k = new double[n];
        for (var i = 0; i < n; i++)
        {
            var v = lap.Speed[i];
            if (v < MinSpeedForCurvature)
            {
                k[i] = 0;
                continue;
            }

            k[i] = useYaw ? Math.Abs(lap.YawRate[i]) / v : Math.Abs(lap.LatAccel[i]) / (v * v);
        }

        return k;
    }

    internal static double[] Smooth(double[] values, int window)
    {
        if (window <= 1)
        {
            return values;
        }

        var n = values.Length;
        var half = window / 2;
        var result = new double[n];
        var prefix = new double[n + 1];
        for (var i = 0; i < n; i++)
        {
            prefix[i + 1] = prefix[i] + values[i];
        }

        for (var i = 0; i < n; i++)
        {
            var a = Math.Max(0, i - half);
            var b = Math.Min(n, i + half + 1);
            result[i] = (prefix[b] - prefix[a]) / (b - a);
        }

        return result;
    }

    /// <summary>
    /// Where the entry to this corner really begins when the driver never got back to full throttle
    /// after the previous bend (130R into the Casio chicane): the braking for this corner, plus the
    /// lift just before it, rather than everything since the last flat-out point.
    /// </summary>
    private static int EntryStart(LapTrace lap, int from, int apex, CornerDetectionOptions o)
    {
        // The braking run that leads into the apex.
        var b = apex;
        while (b > from && lap.Brake[b] <= o.BrakeOn)
        {
            b--;
        }

        if (lap.Brake[b] <= o.BrakeOn)
        {
            return from; // lifted only: keep the whole lift
        }

        while (b > from && lap.Brake[b - 1] > o.BrakeOn)
        {
            b--;
        }

        // Then back over the lift before it, while the throttle was coming off (going forward).
        var limit = Math.Max(from, b - o.MaxLiftBeforeBrakeMetres);
        while (b > limit && lap.Throttle[b - 1] >= lap.Throttle[b] - 0.02f && lap.Brake[b - 1] <= o.BrakeOn)
        {
            b--;
        }

        return b;
    }

    private static bool IsFlatOut(LapTrace lap, int i, CornerDetectionOptions o) =>
        lap.Throttle[i] >= o.FullThrottle && lap.Brake[i] <= o.BrakeOn;

    private static int ArgMin(float[] values, int from, int to)
    {
        var best = from;
        for (var i = from + 1; i <= to; i++)
        {
            if (values[i] < values[best])
            {
                best = i;
            }
        }

        return best;
    }
}

/// <summary>A corner found on one lap, before naming.</summary>
internal readonly record struct CornerCandidate(int StartM, int ApexM, int EndM, float MinSpeed);
