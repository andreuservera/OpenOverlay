namespace IRacingOverlay.Sdk.Interop;

/// <summary>
/// Fixed layout constants for iRacing's shared-memory block (irsdk_header / irsdk_varHeader),
/// as published in iRacing's SDK headers for third-party telemetry tools.
/// </summary>
internal static class IrsdkConstants
{
    public const string MemoryMappedFileName = "Local\\IRSDKMemMapFileName";
    public const string DataValidEventName = "Local\\IRSDKDataValidEvent";

    public const int MaxBufs = 4;
    public const int MaxString = 32;
    public const int MaxDesc = 64;

    // irsdk_header field offsets (bytes)
    public const int HeaderVerOffset = 0;
    public const int HeaderStatusOffset = 4;
    public const int HeaderTickRateOffset = 8;
    public const int HeaderSessionInfoUpdateOffset = 12;
    public const int HeaderSessionInfoLenOffset = 16;
    public const int HeaderSessionInfoOffsetOffset = 20;
    public const int HeaderNumVarsOffset = 24;
    public const int HeaderVarHeaderOffsetOffset = 28;
    public const int HeaderNumBufOffset = 32;
    public const int HeaderBufLenOffset = 36;
    // 8 bytes padding at 40..47
    public const int HeaderVarBufArrayOffset = 48;
    public const int VarBufEntrySize = 16; // tickCount(4) + bufOffset(4) + pad[2](8)
    public const int HeaderTotalSize = HeaderVarBufArrayOffset + MaxBufs * VarBufEntrySize; // 112

    // irsdk_varHeader field offsets (bytes), entry size 144
    public const int VarHeaderTypeOffset = 0;
    public const int VarHeaderOffsetOffset = 4;
    public const int VarHeaderCountOffset = 8;
    public const int VarHeaderCountAsTimeOffset = 12;
    public const int VarHeaderNameOffset = 16;
    public const int VarHeaderDescOffset = VarHeaderNameOffset + MaxString; // 48
    public const int VarHeaderUnitOffset = VarHeaderDescOffset + MaxDesc;   // 112
    public const int VarHeaderEntrySize = VarHeaderUnitOffset + MaxString; // 144

    public const int StatusConnected = 1;
}
