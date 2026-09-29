using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk.Tests;

public class IrsdkParserValidationTests
{
    private static IrsdkHeader Header(
        int numVars = 2,
        int varHeaderOffset = 112,
        int sessionInfoOffset = 400,
        int sessionInfoLen = 100,
        int numBuf = 2,
        int bufLen = 64,
        int[]? bufOffsets = null)
    {
        var offsets = bufOffsets ?? [600, 700];
        return new IrsdkHeader
        {
            Version = 2,
            Status = 1,
            TickRate = 60,
            SessionInfoUpdate = 1,
            SessionInfoLen = sessionInfoLen,
            SessionInfoOffset = sessionInfoOffset,
            NumVars = numVars,
            VarHeaderOffset = varHeaderOffset,
            NumBuf = numBuf,
            BufLen = bufLen,
            VarBufs = Enumerable.Range(0, IrsdkConstants.MaxBufs)
                .Select(i => new IrsdkVarBuf(1, i < offsets.Length ? offsets[i] : 0))
                .ToList(),
        };
    }

    [Fact]
    public void IsPlausible_ConsistentHeader_True() => Assert.True(IrsdkParser.IsPlausible(Header(), 4096));

    [Theory]
    [InlineData(0, 112, 400, 100, 2, 64)]        // no variables yet
    [InlineData(2, 4000, 400, 100, 2, 64)]       // var headers run past the mapping
    [InlineData(2, 112, 4050, 100, 2, 64)]       // session info runs past the mapping
    [InlineData(2, 112, 400, -1, 2, 64)]         // negative length
    [InlineData(2, 112, 400, 100, 0, 64)]        // no buffers
    [InlineData(2, 112, 400, 100, 5, 64)]        // more buffers than the format has
    [InlineData(2, 112, 400, 100, 2, 0)]         // empty buffers
    [InlineData(2, -8, 400, 100, 2, 64)]         // negative offset
    public void IsPlausible_InconsistentHeader_False(int numVars, int varHeaderOffset, int sessionInfoOffset, int sessionInfoLen, int numBuf, int bufLen)
    {
        var header = Header(numVars, varHeaderOffset, sessionInfoOffset, sessionInfoLen, numBuf, bufLen);

        Assert.False(IrsdkParser.IsPlausible(header, 4096));
    }

    [Fact]
    public void IsPlausible_TickBufferPastTheMapping_False()
    {
        Assert.False(IrsdkParser.IsPlausible(Header(bufOffsets: [600, 4090]), 4096));
    }

    [Fact]
    public void BuildVarMap_DropsEntriesThatWouldReadOutsideTheBuffer()
    {
        IrsdkVarHeader Var(string name, IrsdkVarType type, int offset, int count = 1) => new()
        {
            Name = name,
            Type = type,
            Offset = offset,
            Count = count,
            CountAsTime = false,
            Description = "",
            Unit = "",
        };

        var map = IrsdkParser.BuildVarMap(
        [
            Var("Speed", IrsdkVarType.Float, 0),
            Var("Speed", IrsdkVarType.Int, 4),               // duplicate name: first wins
            Var("Rpm", IrsdkVarType.Float, 60),              // exactly fits a 64-byte buffer
            Var("Overflow", IrsdkVarType.Double, 60),        // 8 bytes from 60 does not
            Var("Array", IrsdkVarType.Float, 8, count: 20),  // 80 bytes from 8 does not
            Var("", IrsdkVarType.Int, 12),                   // unnamed
            Var("Weird", (IrsdkVarType)99, 16),              // unknown type
            Var("Negative", IrsdkVarType.Int, -4),
        ], bufLen: 64);

        Assert.Equal(["Rpm", "Speed"], map.Keys.Order());
        Assert.Equal(IrsdkVarType.Float, map["Speed"].Type);
    }
}
