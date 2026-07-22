using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

public sealed class DeltaState
{
    public required double DeltaSeconds { get; init; }
    /// <summary>Seconds of gap per second of real time — negative means currently gaining on the
    /// reference lap, positive means currently losing ground, zero means holding steady. Drives the
    /// display color's intensity (how saturated the green/red is), separately from DeltaSeconds
    /// itself (which drives the displayed number).</summary>
    public required double RateOfChange { get; init; }
    public required bool IsValid { get; init; }
    public required string ReferenceLabel { get; init; }

    public string Display => IsValid
        ? (DeltaSeconds <= 0
            ? $"-{Math.Abs(DeltaSeconds).ToString("0.000", CultureInfo.InvariantCulture)}"
            : $"+{DeltaSeconds.ToString("0.000", CultureInfo.InvariantCulture)}")
        : "—";
}
