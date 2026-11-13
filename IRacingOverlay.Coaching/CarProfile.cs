using IRacingOverlay.Coaching.Advice;

namespace IRacingOverlay.Coaching;

/// <summary>Families of cars that need different thresholds.</summary>
public enum CarClass
{
    /// <summary>GT3, GT4, Cup and other closed-wheel cars with some aero: what the coach was tuned on.</summary>
    Gt,

    /// <summary>Low power and little or no aero (MX-5, GR86, Formula Vee, FF1600…): braking is short,
    /// minimum speed decides most corners, many corners are lifts or flat, differences are small.</summary>
    LowPower,

    /// <summary>Single-seaters with real aero (F4 and up): high corner speeds, grip and corner speed dominate.</summary>
    Formula,
}

/// <summary>
/// The thresholds the coach uses for one <see cref="CarClass"/>: when a corner counts as slow, how
/// big a difference must be to be called out, and how small a loss is noise. Picked from the car in
/// the session (<see cref="CarProfiles.For"/>), with GT as the fallback.
/// </summary>
/// <param name="SlowApexSpeed">Apex speed under this is a slow corner, m/s.</param>
/// <param name="MinLossSeconds">A corner losing less than this is noise.</param>
public sealed record CarProfile(CarClass Class, float SlowApexSpeed, DiagnosisOptions Diagnosis, double MinLossSeconds)
{
    /// <summary>How the class reads in a report or log: "GT", "low power", "formula".</summary>
    public string Label => Class switch { CarClass.LowPower => "low power", CarClass.Formula => "formula", _ => "GT" };
}

public static class CarProfiles
{
    public static CarProfile Gt { get; } = new(CarClass.Gt, Corners.CornerTypes.SlowApexSpeed, new DiagnosisOptions(), 0.05);

    // Low power: everything happens in a narrower band, so smaller differences matter. Slow corners
    // are the ones under 80 km/h; most of the rest are lifts or flat.
    public static CarProfile LowPower { get; } = new(CarClass.LowPower, 80 / 3.6f, new DiagnosisOptions
    {
        BrakePointMetres = 6,
        BrakeRampMetres = 6,
        TrailBrakeMetres = 8,
        ThrottleMetres = 8,
        SpeedKmh = 1.5,
        LiftDepthShare = 0.10f,
        CoastingMetres = 8,
    }, 0.04);

    // Formula: corners are taken much faster, so "slow" starts higher, and speed differences need to
    // be bigger to stand out of the noise; under aero a lot of steering is normal, so understeer
    // needs a larger change to be called.
    public static CarProfile Formula { get; } = new(CarClass.Formula, 130 / 3.6f, new DiagnosisOptions
    {
        BrakePointMetres = 10,
        TrailBrakeMetres = 8,
        SpeedKmh = 3.0,
        UndersteerShare = 0.2,
    }, 0.05);

    private static readonly string[] LowPowerNames =
    [
        "mx-5", "mx5", "miata", "gr86", "gr 86", "toyota 86", "vee", "ff1600", "ray ff", "skip barber", "spec racer",
        "solstice", "mini cooper", "legends", "street stock", "jetta", "kia optima", "renault clio", "fiesta",
    ];

    private static readonly string[] FormulaNames =
    [
        "formula", "f4", "fr2.0", "fr 2.0", "f3", "super formula", "dallara", "indycar", "ir-18", "ir18", "ir-01", "pro mazda",
        "indy pro", "usf", "f1", "mercedes-amg w", "mclaren mp4", "williams fw", "lotus 79", "lotus 49", "tatuus", "fia f4", "pm-18",
    ];

    /// <summary>The profile for a car (iRacing's CarScreenName); GT when it isn't recognised.</summary>
    public static CarProfile For(string? car)
    {
        if (string.IsNullOrWhiteSpace(car))
        {
            return Gt;
        }

        var name = car.ToLowerInvariant();
        if (LowPowerNames.Any(name.Contains))
        {
            return LowPower;
        }

        return FormulaNames.Any(n => Contains(name, n)) ? Formula : Gt;
    }

    // Short names (f1, f3, f4) only as whole words, so "Ferrari 296" isn't "f2…" and "BMW M4 GT3" isn't a formula car.
    private static bool Contains(string name, string word)
    {
        if (word.Length > 3)
        {
            return name.Contains(word, StringComparison.Ordinal);
        }

        var i = name.IndexOf(word, StringComparison.Ordinal);
        while (i >= 0)
        {
            var before = i == 0 || !char.IsLetterOrDigit(name[i - 1]);
            var after = i + word.Length == name.Length || !char.IsLetterOrDigit(name[i + word.Length]);
            if (before && after)
            {
                return true;
            }

            i = name.IndexOf(word, i + 1, StringComparison.Ordinal);
        }

        return false;
    }
}
