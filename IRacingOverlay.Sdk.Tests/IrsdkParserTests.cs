using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk.Tests;

public class IrsdkParserTests
{
    private const string SampleYaml = "WeekendInfo:\n  TrackName: testtrack\nDriverInfo:\n  DriverCarIdx: 0\n";

    private static byte[] BuildImage(out SyntheticMemoryBuilder builder, out int bufLen)
    {
        builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float, unit: "m/s");
        builder.AddVar("Lap", IrsdkVarType.Int);
        builder.AddVar("IsOnTrack", IrsdkVarType.Bool);
        builder.AddVar("CarIdxLapDistPct", IrsdkVarType.Float, count: 3);

        bufLen = 4 + 4 + 1 + 3 * 4; // matches sequential var layout
        var tick0 = builder.BuildTickBuffer(bufLen, w =>
        {
            w.SetFloat("Speed", 42.5f);
            w.SetInt("Lap", 3);
            w.SetBool("IsOnTrack", true);
            w.SetFloatArray("CarIdxLapDistPct", [0.1f, 0.2f, 0.3f]);
        });

        return builder.BuildFullImage(
            tickRate: 60,
            connected: true,
            sessionInfoYaml: SampleYaml,
            bufLen: bufLen,
            tickBuffers: [(tickCount: 7, data: tick0)]);
    }

    [Fact]
    public void ParseHeader_RoundTripsAllFields()
    {
        var image = BuildImage(out _, out var bufLen);

        var header = IrsdkParser.ParseHeader(image);

        Assert.Equal(60, header.TickRate);
        Assert.True(header.IsConnected);
        Assert.Equal(4, header.NumVars);
        Assert.Equal(bufLen, header.BufLen);
        Assert.Equal(1, header.NumBuf);
        // VarBufs always exposes all 4 fixed slots (matches iRacing's fixed-size array); NumBuf says how many are live.
        Assert.Equal(4, header.VarBufs.Count);
        Assert.Equal(7, header.VarBufs[0].TickCount);
        Assert.Equal(IrsdkConstants.HeaderTotalSize, header.VarHeaderOffset);
    }

    [Fact]
    public void ParseHeader_DisconnectedStatus_ReportsNotConnected()
    {
        var builder = new SyntheticMemoryBuilder();
        var image = builder.BuildFullImage(60, connected: false, sessionInfoYaml: "x: 1\n", bufLen: 16,
            tickBuffers: [(0, new byte[16])]);

        var header = IrsdkParser.ParseHeader(image);

        Assert.False(header.IsConnected);
    }

    [Fact]
    public void ParseVarHeaders_RoundTripsNameTypeCountOffsetUnit()
    {
        var image = BuildImage(out _, out _);
        var header = IrsdkParser.ParseHeader(image);

        var varHeaders = IrsdkParser.ParseVarHeaders(image, header.VarHeaderOffset, header.NumVars);

        Assert.Equal(4, varHeaders.Count);

        var speed = varHeaders.Single(v => v.Name == "Speed");
        Assert.Equal(IrsdkVarType.Float, speed.Type);
        Assert.Equal(0, speed.Offset);
        Assert.Equal(1, speed.Count);
        Assert.Equal("m/s", speed.Unit);

        var lap = varHeaders.Single(v => v.Name == "Lap");
        Assert.Equal(IrsdkVarType.Int, lap.Type);
        Assert.Equal(4, lap.Offset);

        var distPct = varHeaders.Single(v => v.Name == "CarIdxLapDistPct");
        Assert.Equal(3, distPct.Count);
    }

    [Fact]
    public void ReadSessionInfoYaml_ExtractsExactTextAndTrimsNulPadding()
    {
        var image = BuildImage(out _, out _);
        var header = IrsdkParser.ParseHeader(image);

        var yaml = IrsdkParser.ReadSessionInfoYaml(image, header.SessionInfoOffset, header.SessionInfoLen);

        Assert.Equal(SampleYaml, yaml);
    }

    [Fact]
    public void ReadSessionInfoYaml_TrimsTrailingNulPadding()
    {
        var text = "a: 1\n"u8.ToArray();
        var padded = new byte[text.Length + 20]; // zero-initialized, i.e. NUL-padded
        text.CopyTo(padded, 0);

        var yaml = IrsdkParser.ReadSessionInfoYaml(padded, 0, padded.Length);

        Assert.Equal("a: 1\n", yaml);
    }
}
