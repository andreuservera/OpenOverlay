namespace IRacingOverlay.Sdk.Interop;

/// <summary>
/// Parsed description of a single telemetry variable (e.g. "Speed", "CarIdxLapDistPct"):
/// where it lives in each tick's data buffer, its type, and how many entries it has.
/// </summary>
public sealed class IrsdkVarHeader
{
    public required IrsdkVarType Type { get; init; }
    public required int Offset { get; init; }
    public required int Count { get; init; }
    public required bool CountAsTime { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Unit { get; init; }
}
