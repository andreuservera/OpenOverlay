using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// The layer that turns distance on track into seconds, shared by Relative and Standings so the same
/// separation can never read as two different gaps: how long a lap takes each class right now.
///
/// Measured laps only. iRacing's CarClassEstLapTime is no longer used anywhere in it — it is a
/// per-car figure (BoP'd models of one class differ), which is how the same separation used to
/// produce different times depending on which car was being looked at.
/// </summary>
internal sealed class ReferencePace
{
    /// <summary>How far off the class's best lap a lap can be and still say how fast the class is
    /// going. Anything slower was an in- or out-lap, a caution lap or an incident.</summary>
    private const double RepresentativeLapMargin = 1.07;

    private readonly LapTimeSource _laps;
    private readonly IReadOnlyList<DriverEntry> _racing;
    private readonly IReadOnlyList<SessionEntry> _earlierSessions;
    private readonly Dictionary<int, double> _byClass = new();

    public ReferencePace(LapTimeSource laps, IReadOnlyList<DriverEntry> racing, TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        _laps = laps;
        _racing = racing;

        var current = CurrentSession.Number(telemetry, session);
        _earlierSessions = (session?.SessionInfo?.Sessions ?? [])
            .Where(s => s.SessionNum < current)
            .OrderByDescending(s => s.SessionNum)
            .ToList();
    }

    /// <summary>Seconds for <paramref name="laps"/> of track at <paramref name="classId"/>'s pace, or
    /// NaN when nobody in the class has a lap to go on yet.</summary>
    public double SecondsFor(double laps, int classId)
    {
        var lapTime = LapTimeOf(classId);
        return lapTime > 0 ? laps * lapTime : double.NaN;
    }

    /// <summary>
    /// How long a lap takes <paramref name="classId"/> right now: the median of every car's latest
    /// lap, leaving out any more than 7% off the class best. The median rather than the mean, so the
    /// odd lap lost in traffic or to a moment off doesn't drag it, and a car finishing a lap only moves
    /// it when it changes the middle of the class. When no latest lap qualifies (the whole class just
    /// ran a caution lap) the class best stands in.
    ///
    /// Before anyone in the class has a lap this session — the opening lap of a race — the class's
    /// median best lap from the weekend's latest earlier session that has any (qualifying, then
    /// practice). 0 with nothing at all.
    /// </summary>
    public double LapTimeOf(int classId)
    {
        if (_byClass.TryGetValue(classId, out var cached))
        {
            return cached;
        }

        var cars = _racing.Where(d => d.CarClassID == classId).Select(d => d.CarIdx).ToHashSet();
        var lapTime = RecentLapTime(cars);
        if (lapTime <= 0)
        {
            lapTime = EarlierSessionLapTime(cars);
        }

        _byClass[classId] = lapTime;
        return lapTime;
    }

    private double RecentLapTime(IReadOnlyCollection<int> cars)
    {
        var best = _laps.FastestOf(cars);
        var recent = cars
            .Select(_laps.Last)
            .Where(last => last > 0 && (best <= 0 || last <= best * RepresentativeLapMargin))
            .ToList();

        return recent.Count > 0 ? Median(recent) : best;
    }

    private double EarlierSessionLapTime(IReadOnlySet<int> cars)
    {
        foreach (var earlier in _earlierSessions)
        {
            var best = earlier.ResultsPositions
                .Where(r => cars.Contains(r.CarIdx) && r.FastestTime > 0)
                .Select(r => r.FastestTime)
                .ToList();
            if (best.Count > 0)
            {
                return Median(best);
            }
        }

        return 0;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }
}
