namespace IRacingOverlay.Sdk.Interop;

/// <summary>
/// Parsed snapshot of the fixed irsdk_header struct at the start of the shared-memory block.
/// </summary>
public sealed class IrsdkHeader
{
    public required int Version { get; init; }
    public required int Status { get; init; }
    public required int TickRate { get; init; }
    public required int SessionInfoUpdate { get; init; }
    public required int SessionInfoLen { get; init; }
    public required int SessionInfoOffset { get; init; }
    public required int NumVars { get; init; }
    public required int VarHeaderOffset { get; init; }
    public required int NumBuf { get; init; }
    public required int BufLen { get; init; }
    public required IReadOnlyList<IrsdkVarBuf> VarBufs { get; init; }

    public bool IsConnected => (Status & IrsdkConstants.StatusConnected) != 0;
}

/// <summary>One of the (typically 4) rotating telemetry buffers iRacing writes ticks into.</summary>
public readonly record struct IrsdkVarBuf(int TickCount, int BufOffset);
