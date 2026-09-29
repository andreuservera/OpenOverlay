using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

public sealed class TireCornerInfo
{
    public required string Label { get; init; }
    public required double PressureKPa { get; init; }
    public required double ColdPressureKPa { get; init; }
    public required double TempLeft { get; init; }
    public required double TempMiddle { get; init; }
    public required double TempRight { get; init; }
    public required bool IsSurfaceTemp { get; init; }

    /// <summary>Remaining tread fraction, 1.0 = new, 0.0 = fully worn. 0 when not reported.</summary>
    public required double WearLeft { get; init; }
    public required double WearMiddle { get; init; }
    public required double WearRight { get; init; }
    public required bool HasWearData { get; init; }

    public UnitSystem UnitSystem { get; init; }

    public string PressureDisplay => PressureKPa > 0
        ? Units.Pressure(PressureKPa, UnitSystem).ToString(Units.PressureFormat(UnitSystem), CultureInfo.InvariantCulture)
        : "—";

    public string PressureUnit => Units.PressureUnit(UnitSystem);
    public string TempLeftDisplay => FormatTemp(TempLeft);
    public string TempMiddleDisplay => FormatTemp(TempMiddle);
    public string TempRightDisplay => FormatTemp(TempRight);

    /// <summary>Worst (lowest-tread) zone of the three — the number that actually matters for "do I
    /// need to pit," since a tire fails at its thinnest point, not its average.</summary>
    public double WorstWearFraction => HasWearData ? Math.Min(WearLeft, Math.Min(WearMiddle, WearRight)) : 1.0;

    // Just the degree sign: three zone temps side by side have no room for the unit letter.
    private string FormatTemp(double c) =>
        c > 0 ? Units.Temperature(c, UnitSystem).ToString("0", CultureInfo.InvariantCulture) + "°" : "—";
}

public sealed class TireInfoState
{
    public required TireCornerInfo LF { get; init; }
    public required TireCornerInfo RF { get; init; }
    public required TireCornerInfo LR { get; init; }
    public required TireCornerInfo RR { get; init; }

    public static TireInfoState Empty { get; } = new()
    {
        LF = EmptyCorner("LF"),
        RF = EmptyCorner("RF"),
        LR = EmptyCorner("LR"),
        RR = EmptyCorner("RR"),
    };

    private static TireCornerInfo EmptyCorner(string label) => new()
    {
        Label = label,
        PressureKPa = 0,
        ColdPressureKPa = 0,
        TempLeft = 0,
        TempMiddle = 0,
        TempRight = 0,
        IsSurfaceTemp = false,
        WearLeft = 1,
        WearMiddle = 1,
        WearRight = 1,
        HasWearData = false,
    };
}
