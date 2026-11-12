using IRacingOverlay.Coaching.Laps;

namespace IRacingOverlay.Coaching.Corners;

/// <summary>Why one pass through one corner can't be used as a reference.</summary>
[Flags]
public enum CornerPassFlaw
{
    None = 0,

    /// <summary>All four wheels off somewhere in the corner or on the straight after it.</summary>
    OffTrack = 1 << 0,

    /// <summary>Incident points were added in the corner or on the straight after it.</summary>
    Incident = 1 << 1,

    /// <summary>On pit road in the corner or on the straight after it.</summary>
    PitRoad = 1 << 2,

    /// <summary>Rejoining after an off just before the corner: the approach was spoilt.</summary>
    AfterOffTrack = 1 << 3,

    /// <summary>One of the first corners of a race start or out lap.</summary>
    StartLap = 1 << 4,

    /// <summary>The car took a clearly shorter path than usual: a cut, with or without an off-track.</summary>
    Shortcut = 1 << 5,

    /// <summary>One of the first corners after leaving pit road: not up to speed yet.</summary>
    PitExit = 1 << 6,

    /// <summary>The car went off on the way into the next corner: this corner's straight was likely
    /// gained by braking too late for it.</summary>
    OffTrackNext = 1 << 7,
}

/// <summary>
/// The per-corner validity rules. A lap with an off at one corner still gives clean passes
/// everywhere else; only the corners the off (or incident, or pit road) touched are voided.
/// A corner is judged on its own window plus the straight after it, because that straight is part
/// of its score (<see cref="CornerPass.ScoreTime"/>).
/// </summary>
public static class CornerChecks
{
    /// <summary>The window checked starts this far before the corner, so an off on the approach
    /// (braking into the gravel) counts against it, metres.</summary>
    public const int ApproachMetres = 50;

    /// <summary>An off ending this far before a corner's approach still spoils the corner, metres.</summary>
    public const int RejoinMetres = 200;

    /// <summary>iRacing adds incident points a moment after the event; a rise this long after the
    /// window still belongs to it, seconds.</summary>
    public const double IncidentDelaySeconds = 1.0;

    /// <summary>On a race start or out lap, this many corners from the line are not representative.</summary>
    public const int StartLapCorners = 2;

    /// <summary>A pass whose path is shorter than the corner's usual path by more than this share
    /// is a cut. The racing line itself moves the ratio by well under 1%; cutting a chicane by 10 m
    /// over 200 m is 5%.</summary>
    public const double ShortcutShare = 0.025;

    /// <summary>The usual path is the median of at least this many passes.</summary>
    public const int ShortcutBaselinePasses = 3;

    /// <summary>The flaws the trace itself shows for a corner running from <paramref name="startM"/>
    /// to <paramref name="scoreEndM"/> (the next corner's start).</summary>
    public static CornerPassFlaw Inspect(LapTrace lap, int startM, int scoreEndM)
    {
        var last = lap.Length - 1;
        var from = Math.Clamp(startM - ApproachMetres, 0, last);
        var to = Math.Clamp(scoreEndM, from, last);
        var flaws = CornerPassFlaw.None;

        for (var i = from; i <= to; i++)
        {
            if (lap.OffTrack[i])
            {
                flaws |= CornerPassFlaw.OffTrack;
            }

            if (lap.PitRoad[i])
            {
                flaws |= CornerPassFlaw.PitRoad;
            }
        }

        var lateEnd = Math.Min(last, to + (int)Math.Ceiling(lap.Speed[to] * IncidentDelaySeconds));
        if (lap.Incidents[lateEnd] > lap.Incidents[from])
        {
            flaws |= CornerPassFlaw.Incident;
        }

        var rejoinFrom = Math.Max(0, from - RejoinMetres);
        for (var i = rejoinFrom; i < from; i++)
        {
            if (lap.OffTrack[i])
            {
                flaws |= CornerPassFlaw.AfterOffTrack;
                break;
            }
        }

        return flaws;
    }

    /// <summary>What voids a stretch of the lap: off-track, pit road or an incident (counted up to
    /// <see cref="IncidentDelaySeconds"/> after it); none when it is clean.</summary>
    public static CornerPassFlaw StretchFlaws(LapTrace lap, int fromM, int toM)
    {
        var last = lap.Length - 1;
        var from = Math.Clamp(fromM, 0, last);
        var to = Math.Clamp(toM, from, last);
        var flaws = CornerPassFlaw.None;
        for (var i = from; i <= to; i++)
        {
            if (lap.OffTrack[i])
            {
                flaws |= CornerPassFlaw.OffTrack;
            }

            if (lap.PitRoad[i])
            {
                flaws |= CornerPassFlaw.PitRoad;
            }
        }

        var lateEnd = Math.Min(last, to + (int)Math.Ceiling(lap.Speed[to] * IncidentDelaySeconds));
        if (lap.Incidents[lateEnd] > lap.Incidents[from])
        {
            flaws |= CornerPassFlaw.Incident;
        }

        return flaws;
    }

    /// <summary>An off between the approach to <paramref name="corner"/> and its slowest point:
    /// running into the gravel on the way in.</summary>
    public static bool OffOnEntry(LapTrace lap, Corner corner)
    {
        var last = lap.Length - 1;
        var from = Math.Clamp(corner.StartM - ApproachMetres, 0, last);
        var to = Math.Clamp(corner.ApexM, from, last);
        for (var i = from; i <= to; i++)
        {
            if (lap.OffTrack[i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// How many corners have started since the car last left pit road, before corner
    /// <paramref name="index"/> of <paramref name="map"/> (0 = the first corner after pit exit);
    /// null when the car wasn't on pit road earlier in the lap.
    /// </summary>
    public static int? CornersSincePitExit(LapTrace lap, CornerMap map, int index)
    {
        var start = Math.Clamp(map.Corners[index].StartM, 0, lap.Length - 1);
        var exit = -1;
        for (var i = start - 1; i >= 0; i--)
        {
            if (lap.PitRoad[i])
            {
                exit = i;
                break;
            }
        }

        if (exit < 0)
        {
            return null;
        }

        var since = 0;
        for (var k = 0; k < index; k++)
        {
            if (map.Corners[k].StartM > exit)
            {
                since++;
            }
        }

        return since;
    }

    /// <summary>Distance travelled (speed × time) from <paramref name="startM"/> to
    /// <paramref name="endM"/> over the distance along the track. LapDist follows the track's centre
    /// line, so a car cutting a corner covers the track distance with a shorter path.</summary>
    public static double PathRatio(LapTrace lap, int startM, int endM)
    {
        if (endM <= startM)
        {
            return 1;
        }

        double travelled = 0;
        for (var i = startM; i < endM; i++)
        {
            travelled += 0.5 * (lap.Speed[i] + lap.Speed[i + 1]) * (lap.Time[i + 1] - lap.Time[i]);
        }

        return travelled / (endM - startM);
    }

    /// <summary>
    /// True when <paramref name="pass"/>'s path through the corner is clearly shorter than the
    /// usual one, the median of <paramref name="others"/> (earlier clean passes of the same corner).
    /// False while there are too few of them to say.
    /// </summary>
    public static bool IsShortcut(CornerPass pass, IEnumerable<CornerPass> others)
    {
        var ratios = others.Select(p => p.PathRatio).OrderBy(r => r).ToList();
        if (ratios.Count < ShortcutBaselinePasses)
        {
            return false;
        }

        var median = ratios.Count % 2 == 1 ? ratios[ratios.Count / 2] : 0.5 * (ratios[ratios.Count / 2 - 1] + ratios[ratios.Count / 2]);
        return pass.PathRatio < median * (1 - ShortcutShare);
    }

    /// <summary>
    /// The stretch from the line to the first corner's start (plus the last fraction of a metre
    /// before the line), which no corner's score covers; null when it was not clean.
    /// </summary>
    public static double? OutsideCornersTime(RecordedLap lap, CornerMap map)
    {
        if (lap.Trace is not { } t || map.Corners.Count == 0 || RunInFlaws(lap, map) != CornerPassFlaw.None)
        {
            return null;
        }

        var first = Math.Clamp(CornerAnalyzer.PieceStart(map, 0), 0, t.Length - 1);
        return t.Time[first] + Math.Max(0, lap.LapTime - t.Time[t.Length - 1]);
    }

    /// <summary>What voids the run from the line to the first corner; none when it is clean.</summary>
    public static CornerPassFlaw RunInFlaws(RecordedLap lap, CornerMap map)
    {
        if (lap.Trace is not { } t || map.Corners.Count == 0)
        {
            return CornerPassFlaw.None;
        }

        var flaws = StretchFlaws(t, 0, Math.Clamp(CornerAnalyzer.PieceStart(map, 0), 0, t.Length - 1));
        if (lap.Flaws.HasFlag(LapFlaw.StartLap))
        {
            flaws |= CornerPassFlaw.StartLap;
        }

        if (t.OffTrack[t.Length - 1])
        {
            flaws |= CornerPassFlaw.OffTrack;
        }

        if (t.PitRoad[t.Length - 1])
        {
            flaws |= CornerPassFlaw.PitRoad;
        }

        return flaws;
    }
}
