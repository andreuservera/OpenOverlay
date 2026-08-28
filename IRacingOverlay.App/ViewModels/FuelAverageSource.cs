namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// Which completed laps feed the average-consumption figure. A whole-session average is the most
/// stable but reacts slowly once conditions change (fuel saving, a new tire set, traffic); the
/// rolling windows trade that stability for responsiveness, which is what most fuel overlays
/// (RaceLab, Kapps) default to for the last stint of a race.
/// </summary>
public enum FuelAverageSource
{
    AllSession,
    Last3Laps,
    Last5Laps,
    Last10Laps,
}

public static class FuelAverageSourceExtensions
{
    /// <summary>How many trailing laps this source averages over. int.MaxValue = the whole session.</summary>
    public static int WindowLaps(this FuelAverageSource source) => source switch
    {
        FuelAverageSource.Last3Laps => 3,
        FuelAverageSource.Last5Laps => 5,
        FuelAverageSource.Last10Laps => 10,
        _ => int.MaxValue,
    };

    public static string Label(this FuelAverageSource source) => source switch
    {
        FuelAverageSource.Last3Laps => "AVG 3",
        FuelAverageSource.Last5Laps => "AVG 5",
        FuelAverageSource.Last10Laps => "AVG 10",
        _ => "AVG",
    };
}
