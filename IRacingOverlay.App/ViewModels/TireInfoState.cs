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

    public string PressureDisplay => PressureKPa > 0 ? PressureKPa.ToString("0", CultureInfo.InvariantCulture) : "—";
    public string TempLeftDisplay => FormatTemp(TempLeft);
    public string TempMiddleDisplay => FormatTemp(TempMiddle);
    public string TempRightDisplay => FormatTemp(TempRight);

    private static string FormatTemp(double c) => c > 0 ? c.ToString("0", CultureInfo.InvariantCulture) + "°" : "—";
}

public sealed class TireInfoState
{
    public required TireCornerInfo LF { get; init; }
    public required TireCornerInfo RF { get; init; }
    public required TireCornerInfo LR { get; init; }
    public required TireCornerInfo RR { get; init; }
}
