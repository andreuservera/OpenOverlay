namespace IRacingOverlay.App.TrackMaps;

/// <summary>One telemetry tick of the player's car, as much as the recorder needs.</summary>
public readonly record struct OutlineSample(
    double LapDistPct, double SpeedMps, double YawNorth, double SessionTime, bool OnPitRoad, bool OnTrack);

/// <summary>
/// Learns a track's shape from one clean lap of live telemetry. Live telemetry has no position, only
/// speed and heading, so the lap is dead-reckoned: each tick moves the car speed × time along its
/// heading (YawNorth, clockwise from north). That drifts a little over a lap, so the small gap left
/// between where the lap ends and where it started is spread back along the lap, and a lap whose
/// gap is too big to be honest drift is thrown away.
///
/// A lap counts from one crossing of the line to the next, flat out or not, as long as the car stays
/// on track, off pit road, and LapDistPct moves steadily forward (no tow, reset or long pause).
/// </summary>
public sealed class TrackOutlineRecorder
{
    /// <summary>Points stored per outline.</summary>
    public const int Points = 600;

    /// <summary>The most the lap may fail to close, as a share of its length.</summary>
    public const double MaxClosureShare = 0.08;

    private const double MinLapMetres = 500;
    private const int MinSamples = 200;
    private const double MaxTickSeconds = 0.5;
    private const double MaxForwardStep = 0.03;
    private const double MaxBackwardStep = 0.005;

    private readonly List<(double Pct, double X, double Y, double Dist)> _samples = [];
    private bool _started;
    private bool _hasLast;
    private double _lastPct;
    private double _lastTime;
    private double _lastYaw;
    private double _x;
    private double _y;
    private double _dist;

    /// <summary>How far round the lap being learned the car is, or null when no lap is under way.</summary>
    public double? Progress => _started && _samples.Count > 0 ? Math.Clamp(_lastPct, 0, 1) : null;

    public void Reset()
    {
        _started = false;
        _hasLast = false;
        _samples.Clear();
    }

    /// <summary>Feeds one tick. Returns the outline when this tick completes a clean lap.</summary>
    public TrackOutline? Observe(in OutlineSample s, string track)
    {
        if (!s.OnTrack || s.OnPitRoad || s.LapDistPct < 0 || !double.IsFinite(s.SpeedMps) || !double.IsFinite(s.YawNorth))
        {
            Reset();
            return null;
        }

        if (!_hasLast)
        {
            Remember(s);
            return null;
        }

        var dt = s.SessionTime - _lastTime;
        if (dt <= 0)
        {
            // Paused, or the same tick again: nothing moved.
            return null;
        }

        if (dt > MaxTickSeconds)
        {
            Reset();
            Remember(s);
            return null;
        }

        TrackOutline? finished = null;
        var crossed = _lastPct > 0.9 && s.LapDistPct < 0.1;
        if (crossed)
        {
            if (_started)
            {
                Advance(s, dt);
                _samples.Add((s.LapDistPct + 1, _x, _y, _dist));
                finished = Build(track);
            }

            // Every crossing starts a new lap, finished or not.
            _started = true;
            _samples.Clear();
            _x = _y = _dist = 0;
            _samples.Add((s.LapDistPct, 0, 0, 0));
            Remember(s);
            return finished;
        }

        var step = s.LapDistPct - _lastPct;
        if (step > MaxForwardStep || step < -MaxBackwardStep)
        {
            // A tow, a reset or a spin backwards: this lap's shape can't be trusted.
            Reset();
            Remember(s);
            return null;
        }

        if (_started)
        {
            Advance(s, dt);
            // Only further on than the last point kept, so the points stay in lap order.
            if (s.LapDistPct > _samples[^1].Pct)
            {
                _samples.Add((s.LapDistPct, _x, _y, _dist));
            }
        }

        Remember(s);
        return null;
    }

    private void Remember(in OutlineSample s)
    {
        _hasLast = true;
        _lastPct = s.LapDistPct;
        _lastTime = s.SessionTime;
        _lastYaw = s.YawNorth;
    }

    private void Advance(in OutlineSample s, double dt)
    {
        // The heading half way through the tick, the shorter way round between the two readings.
        var turn = Math.IEEERemainder(s.YawNorth - _lastYaw, 2 * Math.PI);
        var heading = _lastYaw + turn / 2;
        var metres = Math.Max(0, s.SpeedMps) * dt;
        _x += metres * Math.Sin(heading);
        _y += metres * Math.Cos(heading);
        _dist += metres;
    }

    private TrackOutline? Build(string track)
    {
        if (_samples.Count < MinSamples || _dist < MinLapMetres)
        {
            return null;
        }

        var first = _samples[0];
        var last = _samples[^1];
        var gapX = last.X - first.X;
        var gapY = last.Y - first.Y;
        // The end sits a few hundredths of a lap past where the start was, so allow for that much
        // travel before calling the rest drift.
        var overshoot = (last.Pct - 1 - first.Pct) * _dist;
        var gap = Math.Max(0, Math.Sqrt(gapX * gapX + gapY * gapY) - Math.Abs(overshoot));
        var closure = gap / _dist;
        if (closure > MaxClosureShare)
        {
            return null;
        }

        // Spread the gap back along the lap so it closes on itself.
        var pct = new double[_samples.Count];
        var xs = new double[_samples.Count];
        var ys = new double[_samples.Count];
        for (var i = 0; i < _samples.Count; i++)
        {
            var share = _samples[i].Dist / _dist;
            pct[i] = _samples[i].Pct;
            xs[i] = _samples[i].X - gapX * share;
            ys[i] = _samples[i].Y - gapY * share;
        }

        var outX = new float[Points];
        var outY = new float[Points];
        for (var k = 0; k < Points; k++)
        {
            var target = (double)k / Points;
            if (target < pct[0])
            {
                target += 1;
            }

            var (x, y) = Interpolate(pct, xs, ys, target);
            outX[k] = (float)x;
            outY[k] = (float)y;
        }

        // Centre it, so the stored numbers say nothing about where the lap happened to start.
        float minX = outX.Min(), maxX = outX.Max(), minY = outY.Min(), maxY = outY.Max();
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        for (var k = 0; k < Points; k++)
        {
            outX[k] = MathF.Round(outX[k] - cx, 1);
            outY[k] = MathF.Round(outY[k] - cy, 1);
        }

        return new TrackOutline(track, outX, outY, Math.Round(_dist), Math.Round(closure, 4));
    }

    private static (double X, double Y) Interpolate(double[] pct, double[] xs, double[] ys, double target)
    {
        var hi = Array.BinarySearch(pct, target);
        if (hi >= 0)
        {
            return (xs[hi], ys[hi]);
        }

        hi = ~hi;
        if (hi <= 0)
        {
            return (xs[0], ys[0]);
        }

        if (hi >= pct.Length)
        {
            return (xs[^1], ys[^1]);
        }

        var lo = hi - 1;
        var t = (target - pct[lo]) / (pct[hi] - pct[lo]);
        return (xs[lo] + (xs[hi] - xs[lo]) * t, ys[lo] + (ys[hi] - ys[lo]) * t);
    }
}
