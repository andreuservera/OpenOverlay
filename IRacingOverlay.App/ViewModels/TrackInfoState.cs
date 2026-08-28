using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

public sealed class TrackInfoState
{
    public required string TrackName { get; init; }
    public required string SessionLabel { get; init; }
    public required string TrackUsage { get; init; }
    public required double AirTempC { get; init; }
    public required double TrackTempC { get; init; }
    public required double WindSpeedMs { get; init; }
    public required double WindDirRad { get; init; }
    public required double HumidityPct { get; init; }
    /// <summary>Null when the session has no time limit (lap-limited instead).</summary>
    public required double? TimeRemainingSeconds { get; init; }
    /// <summary>Null when the session has no lap limit (time-limited instead).</summary>
    public required int? LapsRemaining { get; init; }

    public static TrackInfoState Empty => new()
    {
        TrackName = "",
        SessionLabel = "",
        TrackUsage = "",
        AirTempC = 0,
        TrackTempC = 0,
        WindSpeedMs = 0,
        WindDirRad = 0,
        HumidityPct = 0,
        TimeRemainingSeconds = null,
        LapsRemaining = null,
    };

    public string TrackNameDisplay => string.IsNullOrWhiteSpace(TrackName) ? "—" : TrackName;

    public string SessionLabelDisplay => string.IsNullOrWhiteSpace(SessionLabel) ? "—" : SessionLabel.ToUpperInvariant();

    private const string UsageSuffix = " usage";

    // iRacing only publishes this ordered wording — there is no numeric rubber/usage value anywhere
    // in the telemetry or session YAML, so the bar is driven off the scale position.
    private static readonly (string State, int Level)[] UsageScale =
    [
        ("clean", 0),
        ("very low usage", 1),
        ("low usage", 2),
        ("moderately low usage", 3),
        ("moderate usage", 4),
        ("moderately high usage", 5),
        ("high usage", 6),
        ("very high usage", 7),
        ("extreme usage", 7),
    ];

    public const int TrackUsageLevelCount = 8;

    /// <summary>Null for states outside the scale, such as "carry over", so the bar stays empty
    /// rather than implying a level iRacing never reported.</summary>
    public int? TrackUsageLevel
    {
        get
        {
            foreach (var (state, level) in UsageScale)
            {
                if (state.Equals(TrackUsage, StringComparison.OrdinalIgnoreCase))
                {
                    return level;
                }
            }

            return null;
        }
    }

    // iRacing phrases these as "moderately low usage"; the panel's own label already says USAGE.
    public string TrackUsageDisplay => string.IsNullOrWhiteSpace(TrackUsage)
        ? "—"
        : (TrackUsage.EndsWith(UsageSuffix, StringComparison.OrdinalIgnoreCase)
            ? TrackUsage[..^UsageSuffix.Length]
            : TrackUsage).ToUpperInvariant();

    public string AirTempDisplay => $"{AirTempC.ToString("0.#", CultureInfo.InvariantCulture)}°C";

    public string TrackTempDisplay => $"{TrackTempC.ToString("0.#", CultureInfo.InvariantCulture)}°C";

    public string WindDisplay => $"{(WindSpeedMs * 3.6).ToString("0.#", CultureInfo.InvariantCulture)} km/h {WindDirectionDisplay}";

    public string WindDirectionDisplay => CompassPoints[(int)Math.Round(NormalizedWindDegrees / 22.5) % CompassPoints.Length];

    private double NormalizedWindDegrees
    {
        get
        {
            var degrees = WindDirRad * (180.0 / Math.PI) % 360;
            return degrees < 0 ? degrees + 360 : degrees;
        }
    }

    private static readonly string[] CompassPoints =
        ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"];

    public string HumidityDisplay => $"{HumidityPct.ToString("0", CultureInfo.InvariantCulture)}%";

    public string TimeRemainingDisplay => TimeRemainingSeconds is { } seconds ? FormatCountdown(seconds) : "—";

    public string LapsRemainingDisplay => LapsRemaining is { } laps ? laps.ToString(CultureInfo.InvariantCulture) : "—";

    private static string FormatCountdown(double seconds)
    {
        var clamped = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return clamped.TotalHours >= 1
            ? $"{(int)clamped.TotalHours}:{clamped.Minutes:00}:{clamped.Seconds:00}"
            : $"{clamped.Minutes}:{clamped.Seconds:00}";
    }
}
