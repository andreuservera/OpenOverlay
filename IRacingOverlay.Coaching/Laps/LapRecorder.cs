using IRacingOverlay.Coaching.Telemetry;

namespace IRacingOverlay.Coaching.Laps;

/// <summary>
/// Turns a stream of telemetry frames into completed laps on a 1 m distance grid.
///
/// A lap runs from one line crossing to the next; the crossing instant is interpolated between the
/// two ticks either side of the line, so lap times are not quantised to 1/60 s. A lap is clean only
/// when all of it was driven at speed on track, with the same rules as the app's fuel/lap log:
/// no pit road, no off-track, no incident, nothing outside the racing session states, no telemetry
/// gap, and not the partial lap recording began in (see <see cref="LapFlaw"/>).
///
/// Feed it frames in order with <see cref="Observe"/>; it is not thread-safe.
/// </summary>
public sealed class LapRecorder
{
    // Session states that aren't driving at speed: get in car, warmup, parade laps, cool down.
    private static readonly int[] NonRacingStates = [1, 2, 3, 6];

    // Ticks are 1/60 s apart; anything much longer means the stream stopped for a while.
    private const double MaxTickGapSeconds = 0.5;

    // How far back LapDist may jump without a line crossing before it counts as a reset/teleport.
    private const float BackwardsToleranceMetres = 20f;

    // How long after the line to wait for iRacing to publish its own lap time.
    private const double ReportedTimeWaitSeconds = 3.0;

    private readonly List<RecordedLap> _laps = [];
    private readonly List<TelemetryFrame> _samples = [];
    private readonly List<double> _distances = [];
    private readonly List<(RecordedLap Lap, double CrossedAt, float LastLapTimeAtCrossing)> _awaitingReportedTime = [];
    private readonly TrackLengthEstimator _trackLength;

    private TelemetryFrame? _prev;
    private double? _lapStart;
    private double _firstSampleTime;
    private int _lapNumber;
    private int _sessionNum;
    private int _incidentsAtStart;
    private LapFlaw _flaws;

    /// <param name="trackLengthMetres">The track length when known (e.g. from the session YAML);
    /// otherwise it is estimated from LapDist / LapDistPct as the car drives.</param>
    public LapRecorder(double? trackLengthMetres = null)
    {
        _trackLength = new TrackLengthEstimator(trackLengthMetres);
    }

    /// <summary>Raised for every completed lap, clean or not, as soon as the car crosses the line.</summary>
    public event Action<RecordedLap>? LapCompleted;

    public IReadOnlyList<RecordedLap> Laps => _laps;

    /// <summary>Best current estimate of the track length in metres; 0 before any is known.</summary>
    public double TrackLength => _trackLength.Value;

    public void Observe(in TelemetryFrame f)
    {
        if (_prev is { } last && (f.SessionNum != last.SessionNum || f.SessionTime < last.SessionTime))
        {
            // New session, or time went backwards (replay rewound, sim restarted): nothing carries over.
            Reset();
        }

        _trackLength.Observe(f);
        ResolveReportedTimes(f);

        if (_prev is not { } prev)
        {
            BeginPartialLap(f);
            _prev = f;
            return;
        }

        if (f.SessionTime - prev.SessionTime > MaxTickGapSeconds)
        {
            _flaws |= LapFlaw.TelemetryGap;
        }

        if (prev.LapDistPct > 0.75f && f.LapDistPct < 0.25f)
        {
            OnLineCrossed(prev, f);
        }
        else
        {
            if (f.LapDist < prev.LapDist - BackwardsToleranceMetres)
            {
                _flaws |= LapFlaw.Discontinuity;
            }

            Add(f, f.LapDist);
        }

        CheckFlaws(f);
        _prev = f;
    }

    /// <summary>Stops waiting for iRacing's reported lap times. The lap in progress is dropped.</summary>
    public void Flush()
    {
        _awaitingReportedTime.Clear();
    }

    private void OnLineCrossed(in TelemetryFrame prev, in TelemetryFrame f)
    {
        var length = _trackLength.Value > 0 ? _trackLength.Value : prev.LapDist / Math.Max(prev.LapDistPct, 1e-3);
        // In the pit lane LapDistPct can run slightly past 1 before it wraps, and slightly below 0
        // after: clamp both sides so the crossing stays between the two ticks.
        var before = Math.Max(0.0, 1.0 - prev.LapDistPct);
        var after = Math.Max(0.0, f.LapDistPct);
        var fraction = before + after > 1e-9 ? before / (before + after) : 0.5;
        var crossedAt = prev.SessionTime + (f.SessionTime - prev.SessionTime) * fraction;

        // Keep the distances either side of the line in order, whatever the pit lane reports.
        var prevEnd = _distances.Count > 0 ? _distances[^1] : prev.LapDist;
        Add(f, Math.Max(f.LapDist + length, prevEnd));
        // The lap after one spent outside racing (gridding, formation lap, pit stall) is a start or
        // out lap: driven from low speed, in traffic or on cold tyres, so not representative.
        var followsNonRacing = (_flaws & (LapFlaw.NotRacing | LapFlaw.NotOnTrack)) != 0;
        FinishLap(crossedAt, length, prev);

        // The new lap starts with the tick before the line, at a negative distance, so the grid's
        // first metre is interpolated rather than extrapolated.
        _samples.Clear();
        _distances.Clear();
        _lapStart = crossedAt;
        _lapNumber = f.Lap;
        _sessionNum = f.SessionNum;
        _incidentsAtStart = prev.IncidentCount;
        _flaws = followsNonRacing ? LapFlaw.StartLap : LapFlaw.None;
        var start = Math.Max(0.0, f.LapDist);
        Add(prev, Math.Min(prev.LapDist - length, Math.Min(0.0, start - 0.01)));
        Add(f, start);
    }

    private void FinishLap(double crossedAt, double length, in TelemetryFrame lastBeforeLine)
    {
        RecordedLap lap;
        if (_lapStart is not { } start)
        {
            lap = new RecordedLap
            {
                LapNumber = _lapNumber,
                SessionNum = _sessionNum,
                StartTime = _firstSampleTime,
                LapTime = crossedAt - _firstSampleTime,
                Flaws = _flaws | LapFlaw.Partial,
            };
        }
        else
        {
            var flaws = _flaws;
            var trace = Resample(start, length);
            if (trace is null)
            {
                flaws |= LapFlaw.TelemetryGap;
            }

            lap = new RecordedLap
            {
                LapNumber = _lapNumber,
                SessionNum = _sessionNum,
                StartTime = start,
                LapTime = crossedAt - start,
                Flaws = flaws,
                Trace = trace,
            };
        }

        _laps.Add(lap);
        _awaitingReportedTime.Add((lap, crossedAt, lastBeforeLine.LapLastLapTime));
        LapCompleted?.Invoke(lap);
    }

    private void BeginPartialLap(in TelemetryFrame f)
    {
        _samples.Clear();
        _distances.Clear();
        _lapStart = null;
        _firstSampleTime = f.SessionTime;
        _lapNumber = f.Lap;
        _sessionNum = f.SessionNum;
        _incidentsAtStart = f.IncidentCount;
        _flaws = LapFlaw.Partial;
        Add(f, f.LapDist);
        CheckFlaws(f);
    }

    private void Add(in TelemetryFrame f, double distance)
    {
        _samples.Add(f);
        _distances.Add(distance);
    }

    private void CheckFlaws(in TelemetryFrame f)
    {
        if (f.OnPitRoad)
        {
            _flaws |= LapFlaw.PitRoad;
        }

        if (f.TrackSurface == TrackSurface.OffTrack)
        {
            _flaws |= LapFlaw.OffTrack;
        }

        if (!f.IsOnTrack || f.TrackSurface == TrackSurface.NotInWorld)
        {
            _flaws |= LapFlaw.NotOnTrack;
        }

        if (f.IncidentCount > _incidentsAtStart)
        {
            _flaws |= LapFlaw.Incident;
        }

        if (Array.IndexOf(NonRacingStates, f.SessionState) >= 0)
        {
            _flaws |= LapFlaw.NotRacing;
        }
    }

    private void ResolveReportedTimes(in TelemetryFrame f)
    {
        for (var i = _awaitingReportedTime.Count - 1; i >= 0; i--)
        {
            var (lap, crossedAt, before) = _awaitingReportedTime[i];
            var changed = f.LapLastLapTime > 0 && f.LapLastLapTime != before;
            var timedOut = f.SessionTime - crossedAt > ReportedTimeWaitSeconds;
            if (changed || timedOut)
            {
                // A timeout with an unchanged value is either two identical laps or no report at all;
                // only the first is worth keeping, and it can't be told apart from a stale value of 0.
                lap.ReportedLapTime = f.LapLastLapTime > 0 ? f.LapLastLapTime : null;
                _awaitingReportedTime.RemoveAt(i);
            }
        }
    }

    /// <summary>The lap's samples on a 1 m grid; null when they don't cover the whole lap.</summary>
    private LapTrace? Resample(double lapStart, double length)
    {
        var n = (int)Math.Floor(length);
        if (n < 2 || _samples.Count < 2)
        {
            return null;
        }

        // Keep strictly increasing distances only (a stationary or rolled-back car repeats them).
        var idx = new List<int>(_samples.Count);
        var lastDist = double.NegativeInfinity;
        for (var i = 0; i < _samples.Count; i++)
        {
            if (_distances[i] > lastDist)
            {
                idx.Add(i);
                lastDist = _distances[i];
            }
        }

        if (idx.Count < 2 || _distances[idx[0]] > 0 || _distances[idx[^1]] < n - 1)
        {
            return null;
        }

        var trace = new LapTrace(n);
        var hasGps = idx.TrueForAll(i => _samples[i].Latitude is not null and not 0d && _samples[i].Longitude is not null and not 0d);
        if (hasGps)
        {
            trace.Latitude = new double[n];
            trace.Longitude = new double[n];
        }

        var k = 0;
        for (var m = 0; m < n; m++)
        {
            while (k < idx.Count - 2 && _distances[idx[k + 1]] < m)
            {
                k++;
            }

            var a = _samples[idx[k]];
            var b = _samples[idx[k + 1]];
            var da = _distances[idx[k]];
            var db = _distances[idx[k + 1]];
            var t = (float)Math.Clamp((m - da) / (db - da), 0, 1);

            trace.Time[m] = (float)(Lerp(a.SessionTime, b.SessionTime, t) - lapStart);
            trace.Speed[m] = Lerp(a.Speed, b.Speed, t);
            trace.Throttle[m] = Lerp(a.Throttle, b.Throttle, t);
            trace.Brake[m] = Lerp(a.Brake, b.Brake, t);
            trace.Steering[m] = Lerp(a.SteeringWheelAngle, b.SteeringWheelAngle, t);
            trace.LatAccel[m] = Lerp(a.LatAccel, b.LatAccel, t);
            trace.LongAccel[m] = Lerp(a.LongAccel, b.LongAccel, t);
            trace.YawRate[m] = Lerp(a.YawRate, b.YawRate, t);
            var nearest = t < 0.5f ? a : b;
            trace.Gear[m] = (sbyte)Math.Clamp(nearest.Gear, sbyte.MinValue, sbyte.MaxValue);
            trace.Abs[m] = nearest.AbsActive;
            if (hasGps)
            {
                trace.Latitude![m] = Lerp(a.Latitude!.Value, b.Latitude!.Value, t);
                trace.Longitude![m] = Lerp(a.Longitude!.Value, b.Longitude!.Value, t);
            }
        }

        return trace;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private void Reset()
    {
        _prev = null;
        _lapStart = null;
        _samples.Clear();
        _distances.Clear();
        _awaitingReportedTime.Clear();
    }
}

/// <summary>Track length from LapDist / LapDistPct: the median of recent mid-lap readings, so a
/// glitchy tick or the pit lane's odd distances can't skew it.</summary>
internal sealed class TrackLengthEstimator
{
    private const int Capacity = 600;
    private readonly double? _known;
    private readonly double[] _ring = new double[Capacity];
    private int _count;
    private int _next;
    private double _cached;
    private bool _dirty;

    public TrackLengthEstimator(double? known)
    {
        _known = known is > 0 ? known : null;
    }

    public double Value
    {
        get
        {
            if (_known is { } k)
            {
                return k;
            }

            if (_dirty && _count > 0)
            {
                var copy = _ring[.._count];
                Array.Sort(copy);
                _cached = copy[_count / 2];
                _dirty = false;
            }

            return _cached;
        }
    }

    public void Observe(in TelemetryFrame f)
    {
        if (_known is not null || f.OnPitRoad || f.LapDistPct is < 0.1f or > 0.9f || f.LapDist <= 0)
        {
            return;
        }

        _ring[_next] = f.LapDist / f.LapDistPct;
        _next = (_next + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
        _dirty = true;
    }
}
