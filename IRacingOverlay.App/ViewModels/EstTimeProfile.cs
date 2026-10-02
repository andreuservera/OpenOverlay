using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// The player's car's CarIdxEstTime curve round the lap: for each point on track, the time iRacing
/// estimates this car takes to get there from the line. Relative uses it to put every car on the
/// player's own clock — "how long would it take me to get where they are" — so a gap to a car of
/// another class follows the player's speed through each corner and straight, exactly as a gap to
/// the same car does.
///
/// iRacing only publishes each car's estimate at that car's current spot, so the curve is learned
/// as it is driven: from the player's own car every tick, and from every car of the same model, whose
/// estimate is the player's. The formation lap alone fills it. Until a stretch of track has been
/// seen, <see cref="EstTimeAt"/> says so and Relative falls back to scaling the other car's own
/// estimate.
/// </summary>
internal sealed class EstTimeProfile
{
    private const int Bins = 1000;

    /// <summary>The widest stretch, as a fraction of the lap, two samples may be apart and still be
    /// joined with a straight line. At 10 Hz a car at 300 km/h covers ~8 m a tick; 2% of even a short
    /// track is several times that.</summary>
    private const double MaxInterpolationSpan = 0.02;

    private readonly (double Pct, double EstTime)?[] _samples = new (double, double)?[Bins];
    private (int SubSessionId, int CarId, double EstLapTime)? _key;

    /// <summary>The lap length of the clock the curve runs on: the player's CarClassEstLapTime.</summary>
    public double EstLapTime => _key?.EstLapTime ?? 0;

    public void Update(TelemetrySnapshot telemetry, IracingSessionInfo? session)
    {
        if (session?.DriverInfo is not { } driverInfo ||
            driverInfo.Drivers.FirstOrDefault(d => d.CarIdx == driverInfo.DriverCarIdx) is not { CarClassEstLapTime: > 0 } player ||
            !telemetry.HasVariable(TelemetryVarNames.CarIdxEstTime) ||
            !telemetry.HasVariable(TelemetryVarNames.CarIdxLapDistPct))
        {
            return;
        }

        // A curve belongs to one car on one track: a new event or a car change starts it over.
        var key = (session.WeekendInfo?.SubSessionID ?? 0, player.CarID, player.CarClassEstLapTime);
        if (_key != key)
        {
            _key = key;
            Array.Clear(_samples);
        }

        var estTimes = telemetry.GetFloatArray(TelemetryVarNames.CarIdxEstTime);
        var lapDistPct = telemetry.GetFloatArray(TelemetryVarNames.CarIdxLapDistPct);
        var onPitRoad = telemetry.HasVariable(TelemetryVarNames.CarIdxOnPitRoad)
            ? telemetry.GetBoolArray(TelemetryVarNames.CarIdxOnPitRoad)
            : null;

        foreach (var driver in driverInfo.Drivers)
        {
            var carIdx = driver.CarIdx;
            if (!SharesClock(driver, player) || carIdx < 0 || carIdx >= estTimes.Length || carIdx >= lapDistPct.Length)
            {
                continue;
            }

            // The pit lane runs off the racing line, so its estimates don't describe the track.
            var pct = (double)lapDistPct[carIdx];
            var estTime = (double)estTimes[carIdx];
            if (pct is < 0 or >= 1 || estTime <= 0 || estTime > player.CarClassEstLapTime ||
                (onPitRoad is not null && carIdx < onPitRoad.Length && onPitRoad[carIdx]))
            {
                continue;
            }

            _samples[(int)(pct * Bins)] = (pct, estTime);
        }
    }

    /// <summary>The same car as the player's — same model, same estimate — so its CarIdxEstTime is
    /// already on the player's clock.</summary>
    public static bool SharesClock(DriverEntry driver, DriverEntry player) =>
        driver.CarID == player.CarID && Math.Abs(driver.CarClassEstLapTime - player.CarClassEstLapTime) < 1e-6;

    /// <summary>The player's estimate at <paramref name="pct"/> of the lap, interpolated between the
    /// nearest samples either side; null where the curve hasn't been seen closely enough yet.</summary>
    public double? EstTimeAt(double pct)
    {
        if (_key is not { EstLapTime: var lapTime } || pct is < 0 or >= 1)
        {
            return null;
        }

        var bin = (int)(pct * Bins);
        var reach = (int)Math.Ceiling(MaxInterpolationSpan * Bins);
        (double Pct, double EstTime)? below = null, above = null;

        for (var step = 0; step <= reach && below is null; step++)
        {
            var index = bin - step;
            if (_samples[(index + Bins) % Bins] is { } sample)
            {
                // Across the line the curve continues from the end of the previous lap.
                var candidate = index < 0 ? (Pct: sample.Pct - 1, EstTime: sample.EstTime - lapTime) : sample;
                if (candidate.Pct <= pct)
                {
                    below = candidate;
                }
            }
        }

        for (var step = 0; step <= reach && above is null; step++)
        {
            var index = bin + step;
            if (_samples[index % Bins] is { } sample)
            {
                var candidate = index >= Bins ? (Pct: sample.Pct + 1, EstTime: sample.EstTime + lapTime) : sample;
                if (candidate.Pct >= pct)
                {
                    above = candidate;
                }
            }
        }

        if (below is not { } from || above is not { } to || to.Pct - from.Pct > MaxInterpolationSpan)
        {
            return null;
        }

        if (to.Pct <= from.Pct)
        {
            return from.EstTime;
        }

        // Just past the line this can come out a hair below zero: a whole lap out, which the gap's
        // fold to the nearest half lap absorbs.
        return from.EstTime + (pct - from.Pct) / (to.Pct - from.Pct) * (to.EstTime - from.EstTime);
    }
}
