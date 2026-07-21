using System.IO.MemoryMappedFiles;
using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk.Tests;

/// <summary>
/// Exercises IRacingConnection.ReadLatestTickWithRetry (the tick-race-free buffer selection
/// logic) against a real memory-mapped file loaded with a synthetic image, standing in for the
/// live shared-memory block iRacing would otherwise provide.
/// </summary>
public class IRacingConnectionRetryTests
{
    private static (MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, IrsdkHeader header, Dictionary<string, IrsdkVarHeader> varsByName)
        LoadImage(byte[] image, IReadOnlyDictionary<string, IrsdkVarHeader> varsByName)
    {
        var mmf = MemoryMappedFile.CreateNew(null, image.Length);
        var accessor = mmf.CreateViewAccessor(0, image.Length, MemoryMappedFileAccess.ReadWrite);
        accessor.WriteArray(0, image, 0, image.Length);

        var header = IrsdkParser.ParseHeader(image);
        return (mmf, accessor, header, new Dictionary<string, IrsdkVarHeader>(varsByName));
    }

    [Fact]
    public void ReadLatestTickWithRetry_PicksBufferWithHighestTickCount()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Marker", IrsdkVarType.Int);
        const int bufLen = 4;

        var buf0 = builder.BuildTickBuffer(bufLen, w => w.SetInt("Marker", 100));
        var buf1 = builder.BuildTickBuffer(bufLen, w => w.SetInt("Marker", 200));
        var buf2 = builder.BuildTickBuffer(bufLen, w => w.SetInt("Marker", 300));
        var buf3 = builder.BuildTickBuffer(bufLen, w => w.SetInt("Marker", 400));

        var image = builder.BuildFullImage(60, true, "x: 1\n", bufLen,
            tickBuffers: [(tickCount: 10, buf0), (tickCount: 25, buf1), (tickCount: 12, buf2), (tickCount: 5, buf3)]);

        var (mmf, accessor, header, varsByName) = LoadImage(image, builder.BuildVarsByName());
        using var _ = mmf;
        using var __ = accessor;

        var snapshot = IRacingConnection.ReadLatestTickWithRetry(accessor, header, varsByName);

        Assert.NotNull(snapshot);
        Assert.Equal(25, snapshot!.TickCount);
        Assert.Equal(200, snapshot.GetInt("Marker"));
    }

    [Fact]
    public void ReadLatestTickWithRetry_AllBuffersUnwritten_ReturnsNull()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Marker", IrsdkVarType.Int);
        const int bufLen = 4;

        var empty = builder.BuildTickBuffer(bufLen, _ => { });
        var image = builder.BuildFullImage(60, true, "x: 1\n", bufLen,
            tickBuffers: [(0, empty), (0, empty), (0, empty), (0, empty)]);

        var (mmf, accessor, header, varsByName) = LoadImage(image, builder.BuildVarsByName());
        using var _ = mmf;
        using var __ = accessor;

        var snapshot = IRacingConnection.ReadLatestTickWithRetry(accessor, header, varsByName);

        Assert.Null(snapshot);
    }
}
