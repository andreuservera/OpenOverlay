using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

public sealed class DeltaState
{
    public required double DeltaSeconds { get; init; }
    public required bool IsValid { get; init; }
    public required string ReferenceLabel { get; init; }

    public string Display => IsValid
        ? (DeltaSeconds <= 0
            ? $"-{Math.Abs(DeltaSeconds).ToString("0.000", CultureInfo.InvariantCulture)}"
            : $"+{DeltaSeconds.ToString("0.000", CultureInfo.InvariantCulture)}")
        : "—";
}
