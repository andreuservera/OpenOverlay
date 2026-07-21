namespace IRacingOverlay.App.ViewModels;

public sealed class TireCornerInfo
{
    public required string Label { get; init; }
    public required double PressureKPa { get; init; }
    public required double ColdPressureKPa { get; init; }
    public required double TempC { get; init; }

    public string PressureDisplay => PressureKPa > 0 ? $"{PressureKPa:0}" : "—";
    public string TempDisplay => TempC > 0 ? $"{TempC:0}°" : "—";
}

public sealed class TireInfoState
{
    public required TireCornerInfo LF { get; init; }
    public required TireCornerInfo RF { get; init; }
    public required TireCornerInfo LR { get; init; }
    public required TireCornerInfo RR { get; init; }
}
