using System.Text;

namespace IRacingOverlay.Sdk.Interop;

/// <summary>
/// Pure byte-buffer parsing for the irsdk shared-memory layout. Kept free of any
/// memory-mapped-file / OS dependency so it can be unit tested against synthetic buffers.
/// </summary>
public static class IrsdkParser
{
    // iRacing writes the session-info YAML blob using the Windows-1252 codepage, not UTF-8 —
    // decoding it as UTF-8 turns any accented or special character (é, ñ, ö, …) into a replacement
    // glyph, since almost no single high-order byte is valid UTF-8 on its own. Registering the
    // codepages provider is required on .NET Core/5+, where 1252 isn't available by default. Both
    // steps must happen in the static constructor's *body* — a field initializer runs before it,
    // not after, so GetEncoding(1252) would fail if it ran as a field initializer instead.
    private static readonly Encoding SessionInfoEncoding;

    static IrsdkParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        SessionInfoEncoding = Encoding.GetEncoding(1252);
    }

    public static IrsdkHeader ParseHeader(ReadOnlySpan<byte> buffer)
    {
        var varBufs = new IrsdkVarBuf[IrsdkConstants.MaxBufs];
        for (var i = 0; i < IrsdkConstants.MaxBufs; i++)
        {
            var entryOffset = IrsdkConstants.HeaderVarBufArrayOffset + i * IrsdkConstants.VarBufEntrySize;
            var tickCount = ReadInt32(buffer, entryOffset);
            var bufOffset = ReadInt32(buffer, entryOffset + 4);
            varBufs[i] = new IrsdkVarBuf(tickCount, bufOffset);
        }

        return new IrsdkHeader
        {
            Version = ReadInt32(buffer, IrsdkConstants.HeaderVerOffset),
            Status = ReadInt32(buffer, IrsdkConstants.HeaderStatusOffset),
            TickRate = ReadInt32(buffer, IrsdkConstants.HeaderTickRateOffset),
            SessionInfoUpdate = ReadInt32(buffer, IrsdkConstants.HeaderSessionInfoUpdateOffset),
            SessionInfoLen = ReadInt32(buffer, IrsdkConstants.HeaderSessionInfoLenOffset),
            SessionInfoOffset = ReadInt32(buffer, IrsdkConstants.HeaderSessionInfoOffsetOffset),
            NumVars = ReadInt32(buffer, IrsdkConstants.HeaderNumVarsOffset),
            VarHeaderOffset = ReadInt32(buffer, IrsdkConstants.HeaderVarHeaderOffsetOffset),
            NumBuf = ReadInt32(buffer, IrsdkConstants.HeaderNumBufOffset),
            BufLen = ReadInt32(buffer, IrsdkConstants.HeaderBufLenOffset),
            VarBufs = varBufs,
        };
    }

    public static List<IrsdkVarHeader> ParseVarHeaders(ReadOnlySpan<byte> buffer, int varHeaderOffset, int numVars)
    {
        var result = new List<IrsdkVarHeader>(numVars);
        for (var i = 0; i < numVars; i++)
        {
            var entryOffset = varHeaderOffset + i * IrsdkConstants.VarHeaderEntrySize;
            var type = (IrsdkVarType)ReadInt32(buffer, entryOffset + IrsdkConstants.VarHeaderTypeOffset);
            var offset = ReadInt32(buffer, entryOffset + IrsdkConstants.VarHeaderOffsetOffset);
            var count = ReadInt32(buffer, entryOffset + IrsdkConstants.VarHeaderCountOffset);
            var countAsTime = buffer[entryOffset + IrsdkConstants.VarHeaderCountAsTimeOffset] != 0;
            var name = ReadFixedString(buffer, entryOffset + IrsdkConstants.VarHeaderNameOffset, IrsdkConstants.MaxString);
            var desc = ReadFixedString(buffer, entryOffset + IrsdkConstants.VarHeaderDescOffset, IrsdkConstants.MaxDesc);
            var unit = ReadFixedString(buffer, entryOffset + IrsdkConstants.VarHeaderUnitOffset, IrsdkConstants.MaxString);

            result.Add(new IrsdkVarHeader
            {
                Type = type,
                Offset = offset,
                Count = count,
                CountAsTime = countAsTime,
                Name = name,
                Description = desc,
                Unit = unit,
            });
        }

        return result;
    }

    public static string ReadSessionInfoYaml(ReadOnlySpan<byte> buffer, int sessionInfoOffset, int sessionInfoLen)
    {
        var slice = buffer.Slice(sessionInfoOffset, sessionInfoLen);
        // The block is null-padded; trim at the first NUL so YAML parsers don't choke on trailing garbage.
        var nul = slice.IndexOf((byte)0);
        if (nul >= 0)
        {
            slice = slice[..nul];
        }

        return SessionInfoEncoding.GetString(slice);
    }

    private static int ReadInt32(ReadOnlySpan<byte> buffer, int offset) =>
        BitConverter.ToInt32(buffer.Slice(offset, 4));

    private static string ReadFixedString(ReadOnlySpan<byte> buffer, int offset, int maxLength)
    {
        var slice = buffer.Slice(offset, maxLength);
        var nul = slice.IndexOf((byte)0);
        if (nul >= 0)
        {
            slice = slice[..nul];
        }

        return Encoding.ASCII.GetString(slice);
    }
}
