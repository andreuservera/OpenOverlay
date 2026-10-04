using System.Globalization;
using IRacingOverlay.Sdk;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Reading iRacing's session clock and lap counter, sentinels included, in one place — the fuel
/// calculator, the track bar and the driver tables' footer all ask the same questions of it.
/// </summary>
internal static class SessionClock
{
    // iRacing reports an implausibly large number rather than a null/-1 sentinel when a session has
    // no lap or time limit.
    public const int NoLapLimitThreshold = 20_000;

    // The time sentinel is a 7-day (604800s) clock, so the threshold has to sit below it — and the
    // longest race iRacing runs is 24h, which makes anything past a day and change a sentinel rather
    // than a session clock. Confirmed live: a 1,000,000s threshold let the sentinel through in test
    // drive and turned it into ~5,500 laps to go, i.e. a "fuel to finish" of thousands of liters.
    public const double NoTimeLimitThresholdSeconds = 25 * 60 * 60;

    /// <summary>Laps left by iRacing's own count; null in a timed or open session.</summary>
    public static int? LapsRemaining(TelemetrySnapshot telemetry)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.SessionLapsRemain))
        {
            return null;
        }

        var raw = telemetry.GetInt(TelemetryVarNames.SessionLapsRemain);
        return raw is > 0 and < NoLapLimitThreshold ? raw : null;
    }

    /// <summary>Seconds left on the session clock; null in a lap-limited or open session.</summary>
    public static double? TimeRemaining(TelemetrySnapshot telemetry)
    {
        if (!telemetry.HasVariable(TelemetryVarNames.SessionTimeRemain))
        {
            return null;
        }

        var raw = telemetry.GetDouble(TelemetryVarNames.SessionTimeRemain);
        return raw >= 0 && raw < NoTimeLimitThresholdSeconds ? raw : null;
    }

    /// <summary>
    /// What a lap of the player's takes right now. Their own recent racing laps first — out laps,
    /// pit laps and the formation lap already left out by <see cref="LapLog"/> — then the last lap
    /// (current fuel load, tyres and traffic), the session best, and before any lap at all iRacing's
    /// own estimate for the car on this track. 0 when there's nothing to go on.
    /// </summary>
    public static double ReferenceLapSeconds(TelemetrySnapshot telemetry, IracingSessionInfo? session, double recentRacingLapSeconds)
    {
        if (recentRacingLapSeconds > 0)
        {
            return recentRacingLapSeconds;
        }

        foreach (var name in new[] { TelemetryVarNames.PlayerLastLapTime, TelemetryVarNames.PlayerBestLapTime })
        {
            var seconds = telemetry.HasVariable(name) ? telemetry.GetFloat(name) : 0;
            if (seconds > 0)
            {
                return seconds;
            }
        }

        var driverInfo = session?.DriverInfo;
        var estimate = driverInfo?.Drivers.FirstOrDefault(d => d.CarIdx == driverInfo.DriverCarIdx)?.CarClassEstLapTime ?? 0;
        return estimate > 0 ? estimate : 0;
    }
}

/// <summary>Where the session is: the player's lap out of how many, and the clock.</summary>
public sealed class SessionProgress
{
    /// <summary>The lap the player is on (laps started); null when unknown.</summary>
    public int? CurrentLap { get; init; }

    /// <summary>The session's lap count when it is lap-limited.</summary>
    public int? TotalLaps { get; init; }

    /// <summary>In a timed session, the laps the player will have run when the clock runs out at their
    /// current pace. Null when it's lap-limited, open, or there's no lap time to go on.</summary>
    public double? EstimatedTotalLaps { get; init; }

    public double? ElapsedSeconds { get; init; }

    /// <summary>The session's length; null in a lap-limited or open session.</summary>
    public double? TotalSeconds { get; init; }

    public static SessionProgress Empty { get; } = new();

    public bool HasLaps => CurrentLap is not null;

    public bool HasTime => ElapsedSeconds is not null;

    /// <summary>"7/24" in a lap race, "7/~23.8" when the total is estimated from the clock (the "~"
    /// marks the estimate), "7" with no end.</summary>
    public string LapDisplay => CurrentLap is not { } lap
        ? "—"
        : TotalLaps is { } total
            ? string.Create(CultureInfo.InvariantCulture, $"{lap}/{total}")
            : EstimatedTotalLaps is { } estimate
                ? string.Create(CultureInfo.InvariantCulture, $"{lap}/~{estimate:0.0}")
                : lap.ToString(CultureInfo.InvariantCulture);

    /// <summary>"23:15 / 1:00:00" — elapsed over the session's length, or just elapsed with no end.</summary>
    public string TimeDisplay => ElapsedSeconds is not { } elapsed
        ? "—"
        : TotalSeconds is { } total
            ? $"{Clock(elapsed, total)} / {Clock(total, total)}"
            : Clock(elapsed, elapsed);

    // Both sides in one format, so "9:41 / 1:00:00" never happens: hours appear when the longer one has them.
    private static string Clock(double seconds, double longest)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return longest >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes:00}:{time.Seconds:00}");
    }
}

internal static class SessionProgressBuilder
{
    public static SessionProgress Build(TelemetrySnapshot telemetry, IracingSessionInfo? session, double recentRacingLapSeconds)
    {
        int? lap = telemetry.HasVariable(TelemetryVarNames.Lap) ? Math.Max(0, telemetry.GetInt(TelemetryVarNames.Lap)) : null;
        var entry = CurrentSession.Entry(telemetry, session);

        var totalLaps = TotalLaps(telemetry, entry);
        var remaining = SessionClock.TimeRemaining(telemetry);
        var totalSeconds = TotalSeconds(telemetry, entry);

        double? elapsed = totalSeconds is { } total && remaining is { } left
            ? Math.Max(0, total - left)
            : telemetry.HasVariable(TelemetryVarNames.SessionTime) ? Math.Max(0, telemetry.GetDouble(TelemetryVarNames.SessionTime)) : null;

        double? estimatedTotal = null;
        if (totalLaps is null && lap is { } current && remaining is { } secondsLeft)
        {
            var pace = SessionClock.ReferenceLapSeconds(telemetry, session, recentRacingLapSeconds);
            if (pace > 0)
            {
                // Distance run so far plus what the clock still allows; never fewer than the lap
                // already under way, which the player completes whatever the clock says.
                estimatedTotal = Math.Max(current, DistanceRun(telemetry, current) + (secondsLeft / pace));
            }
        }

        return new SessionProgress
        {
            // Crossing the line after the flag starts a cool-down lap; the race still read 23/23.
            CurrentLap = totalLaps is { } laps && lap > laps ? laps : lap,
            TotalLaps = totalLaps,
            EstimatedTotalLaps = estimatedTotal,
            ElapsedSeconds = elapsed,
            TotalSeconds = totalSeconds,
        };
    }

    /// <summary>Laps completed plus the fraction of the current one.</summary>
    private static double DistanceRun(TelemetrySnapshot telemetry, int lap)
    {
        var completed = telemetry.HasVariable(TelemetryVarNames.LapCompleted)
            ? telemetry.GetInt(TelemetryVarNames.LapCompleted)
            : lap - 1;
        var fraction = telemetry.HasVariable(TelemetryVarNames.LapDistPct)
            ? Math.Clamp(telemetry.GetFloat(TelemetryVarNames.LapDistPct), 0, 1)
            : 0;
        return Math.Max(0, completed) + fraction;
    }

    private static int? TotalLaps(TelemetrySnapshot telemetry, SessionEntry? entry)
    {
        if (telemetry.HasVariable(TelemetryVarNames.SessionLapsTotal))
        {
            var raw = telemetry.GetInt(TelemetryVarNames.SessionLapsTotal);
            return raw is > 0 and < SessionClock.NoLapLimitThreshold ? raw : null;
        }

        // The YAML says "unlimited" for a timed or open session.
        return int.TryParse(entry?.SessionLaps, NumberStyles.Integer, CultureInfo.InvariantCulture, out var laps) &&
               laps is > 0 and < SessionClock.NoLapLimitThreshold
            ? laps
            : null;
    }

    private static double? TotalSeconds(TelemetrySnapshot telemetry, SessionEntry? entry)
    {
        if (telemetry.HasVariable(TelemetryVarNames.SessionTimeTotal))
        {
            var raw = telemetry.GetDouble(TelemetryVarNames.SessionTimeTotal);
            return raw > 0 && raw < SessionClock.NoTimeLimitThresholdSeconds ? raw : null;
        }

        // The YAML reads "1800.0000 sec", or "unlimited".
        var text = entry?.SessionTime?.Replace("sec", "", StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
               seconds > 0 && seconds < SessionClock.NoTimeLimitThresholdSeconds
            ? seconds
            : null;
    }
}
