namespace IRacingOverlay.Sdk.Interop;

/// <summary>
/// Storage type of a single telemetry variable, as declared by iRacing's shared-memory var header.
/// Numeric values match irsdk_VarType from iRacing's public SDK.
/// </summary>
public enum IrsdkVarType
{
    Char = 0,
    Bool = 1,
    Int = 2,
    BitField = 3,
    Float = 4,
    Double = 5,
}
