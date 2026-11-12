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
/// A corner that straddles the start/finish line is cut at the line; the grid has no wrap-around yet.
/// </summary>
public static class CornerDetector
{
    // Below this speed, yaw rate / speed blows up (pit lane, spins); treat as no curvature data.
    private const float MinSpeedForCurvature = 8f;

    public static CornerMap Detect(LapTrace lap, CornerDetectionOptions? options = null)
    {
        var o = options ?? new CornerDetectionOptions();
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
        var corners = new List<Corner>();
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

            var end = apex;
            while (end < peakAfter && !IsFlatOut(lap, end, o))
            {
                end++;
            }

            corners.Add(new Corner($"T{corners.Count + 1}", start, apex, end));
        }

        return new CornerMap(n, corners);
    }

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
