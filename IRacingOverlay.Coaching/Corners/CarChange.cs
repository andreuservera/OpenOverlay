using IRacingOverlay.Coaching.Laps;

namespace IRacingOverlay.Coaching.Corners;

/// <summary>
/// Spots a car that got slower after an off or contact. iRacing has no damage channel, but damage
/// (or a lost aero part) shows as a lower top speed at the end of every straight, lap after lap.
/// Passes from before that can't be matched any more, so the coach restarts its references.
/// </summary>
public static class CarChange
{
    /// <summary>The stretch before each corner where top speed is read, metres.</summary>
    public const int ApproachMetres = 150;

    /// <summary>Top speed down by at least this share on every lap checked counts as a changed car.</summary>
    public const double DropShare = 0.015;

    /// <summary>Laps after the incident that must all show the drop.</summary>
    public const int LapsToConfirm = 2;

    /// <summary>Corner approaches that must be comparable for a lap to be judged.</summary>
    public const int MinApproaches = 3;

    /// <summary>
    /// Top speed before each corner, by corner id, on the stretches driven flat out and clean.
    /// With <paramref name="beforeFirstIncident"/>, only stretches before the lap's first off or
    /// incident are read (the lap that caused the damage was fine up to there).
    /// </summary>
    public static Dictionary<string, double> ApproachSpeeds(LapTrace t, CornerMap map, bool beforeFirstIncident = false)
    {
        var limit = t.Length;
        if (beforeFirstIncident)
        {
            for (var i = 0; i < t.Length; i++)
            {
                if (t.OffTrack[i] || t.Incidents[i] > 0)
                {
                    limit = i;
                    break;
                }
            }
        }

        var speeds = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var c in map.Corners)
        {
            var to = Math.Min(c.StartM, t.Length) - 1;
            var from = Math.Max(0, c.StartM - ApproachMetres);
            if (to - from < ApproachMetres / 2 || to >= limit)
            {
                continue;
            }

            var flat = 0;
            var clean = true;
            double top = 0;
            for (var i = from; i <= to; i++)
            {
                if (t.OffTrack[i] || t.PitRoad[i])
                {
                    clean = false;
                    break;
                }

                if (t.Throttle[i] >= 0.95f)
                {
                    flat++;
                }

                top = Math.Max(top, t.Speed[i]);
            }

            if (clean && flat >= 0.8 * (to - from + 1))
            {
                speeds[c.Name] = top;
            }
        }

        return speeds;
    }

    /// <summary>The median, over corners seen in both, of this lap's top speed over the usual top
    /// speed before it, minus one (−0.027 = 2.7% slower); null when too few corners compare.</summary>
    public static double? Drop(Dictionary<string, double> lap, IReadOnlyList<Dictionary<string, double>> before)
    {
        var ratios = new List<double>();
        foreach (var (corner, v) in lap)
        {
            var earlier = before.Where(b => b.ContainsKey(corner)).Select(b => b[corner]).ToList();
            if (earlier.Count > 0)
            {
                ratios.Add(v / earlier.Average() - 1);
            }
        }

        if (ratios.Count < MinApproaches)
        {
            return null;
        }

        ratios.Sort();
        return ratios[ratios.Count / 2];
    }
}
