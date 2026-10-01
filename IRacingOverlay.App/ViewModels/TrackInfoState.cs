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

    /// <summary>The player's lap out of the session's laps, the same reading as the tables' footer.</summary>
    public SessionProgress Progress { get; init; } = SessionProgress.Empty;

    public UnitSystem UnitSystem { get; init; }

    /// <summary>False for <see cref="Empty"/>, so readings show as dashes instead of a believable 0.</summary>
    public bool HasData { get; init; } = true;

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
        HasData = false,
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

    public string AirTempDisplay => FormatTemperature(AirTempC);

    public string TrackTempDisplay => FormatTemperature(TrackTempC);

    public string WindDisplay => !HasData
        ? "—"
        : $"{Units.SpeedFromMs(WindSpeedMs, UnitSystem).ToString("0.#", CultureInfo.InvariantCulture)} {Units.SpeedUnit(UnitSystem)} {WindDirectionDisplay}";

    private string FormatTemperature(double celsius) => !HasData
        ? "—"
        : $"{Units.Temperature(celsius, UnitSystem).ToString("0.#", CultureInfo.InvariantCulture)}{Units.TemperatureUnit(UnitSystem)}";

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

    public string HumidityDisplay => HasData ? $"{HumidityPct.ToString("0", CultureInfo.InvariantCulture)}%" : "—";

    public string TimeRemainingDisplay => TimeRemainingSeconds is { } seconds ? FormatCountdown(seconds) : "—";

    /// <summary>"7/23": the lap you're on out of the race's laps. A timed race's total is estimated
    /// from your pace and carries a decimal, "7/23.8".</summary>
    public string LapDisplay => Progress.LapDisplay;

    private static string FormatCountdown(double seconds)
    {
        var clamped = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return clamped.TotalHours >= 1
            ? $"{(int)clamped.TotalHours}:{clamped.Minutes:00}:{clamped.Seconds:00}"
            : $"{clamped.Minutes}:{clamped.Seconds:00}";
    }
}
