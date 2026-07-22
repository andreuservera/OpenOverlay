using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

public sealed class TrackInfoState
{
    public required string TrackName { get; init; }
    public required string SessionLabel { get; init; }
    public required double AirTempC { get; init; }
    public required double TrackTempC { get; init; }
    public required double WindSpeedMs { get; init; }
    public required double HumidityPct { get; init; }
    /// <summary>Null when the session has no time limit (lap-limited instead).</summary>
    public required double? TimeRemainingSeconds { get; init; }
    /// <summary>Null when the session has no lap limit (time-limited instead).</summary>
    public required int? LapsRemaining { get; init; }

    public static TrackInfoState Empty => new()
    {
        TrackName = "",
        SessionLabel = "",
        AirTempC = 0,
        TrackTempC = 0,
        WindSpeedMs = 0,
        HumidityPct = 0,
        TimeRemainingSeconds = null,
        LapsRemaining = null,
    };

    public string TrackNameDisplay => string.IsNullOrWhiteSpace(TrackName) ? "—" : TrackName;

    public string SessionLabelDisplay => string.IsNullOrWhiteSpace(SessionLabel) ? "—" : SessionLabel.ToUpperInvariant();

    public string AirTempDisplay => $"{AirTempC.ToString("0.#", CultureInfo.InvariantCulture)}°C";

    public string TrackTempDisplay => $"{TrackTempC.ToString("0.#", CultureInfo.InvariantCulture)}°C";

    public string WindDisplay => $"{WindSpeedMs.ToString("0.#", CultureInfo.InvariantCulture)} m/s";

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
