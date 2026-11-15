using System.Buffers.Binary;
using System.Text;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Replay;

namespace IRacingOverlay.Sdk.Tests;

public sealed class IbtReplayTests
{
    private const string Yaml = """
        ---
        WeekendInfo:
         TrackName: spa up
        DriverInfo:
         DriverCarIdx: 0
         Drivers:
         - CarIdx: 0
           UserName: Test Driver
        ...
        """;

    /// <summary>An .ibt as iRacing writes one: header, disk sub-header, variable headers, padded
    /// YAML, then one row per tick with Speed = row × 2 and Lap = row.</summary>
    private static byte[] Ibt(int rows, bool writeRecordCount = true)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        builder.AddVar("Lap", IrsdkVarType.Int);
        var bufLen = builder.TotalSize;
        var varTable = builder.BuildFullImage(60, true, "", bufLen, [])
            .AsSpan(IrsdkConstants.HeaderTotalSize, 2 * IrsdkConstants.VarHeaderEntrySize).ToArray();

        var varHeaderOffset = IrsdkConstants.HeaderTotalSize + 32;
        var sessionInfoOffset = varHeaderOffset + varTable.Length;
        var yaml = Encoding.UTF8.GetBytes(Yaml);
        var sessionInfoLen = yaml.Length + 64; // zero padding, as on disk
        var firstRow = sessionInfoOffset + sessionInfoLen;

        var file = new byte[firstRow + rows * bufLen];
        var span = file.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderVerOffset..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderTickRateOffset..], 60);
        BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderSessionInfoLenOffset..], sessionInfoLen);
        BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderSessionInfoOffsetOffset..], sessionInfoOffset);
        BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderNumVarsOffset..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderVarHeaderOffsetOffset..], varHeaderOffset);
        BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderNumBufOffset..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[IrsdkConstants.HeaderBufLenOffset..], bufLen);
        BinaryPrimitives.WriteInt32LittleEndian(span[(IrsdkConstants.HeaderVarBufArrayOffset + 4)..], firstRow);
        if (writeRecordCount)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[(IrsdkConstants.HeaderTotalSize + 28)..], rows);
        }

        varTable.CopyTo(span[varHeaderOffset..]);
        yaml.CopyTo(span[sessionInfoOffset..]);
        for (var i = 0; i < rows; i++)
        {
            builder.BuildTickBuffer(bufLen, w =>
            {
                w.SetFloat("Speed", i * 2f);
                w.SetInt("Lap", i);
            }).CopyTo(span[(firstRow + i * bufLen)..]);
        }

        return file;
    }

    [Fact]
    public void Recording_ReadsHeadersYamlAndRows()
    {
        using var recording = IbtRecording.Open(new MemoryStream(Ibt(120)));

        Assert.Equal(60, recording.TickRate);
        Assert.Equal(2, recording.NumVars);
        Assert.Equal(120, recording.Rows);
        Assert.Equal(TimeSpan.FromSeconds(2), recording.Duration);
        Assert.Equal(Yaml, Encoding.UTF8.GetString(recording.SessionInfo));

        var row = new byte[recording.BufLen];
        recording.ReadRow(7, row);
        var vars = IrsdkParser.BuildVarMap(IrsdkParser.ParseVarHeaders(recording.VarHeaders, 0, 2), recording.BufLen);
        var tick = new TelemetrySnapshot(row, vars, 7);
        Assert.Equal(14f, tick.GetFloat("Speed"));
        Assert.Equal(7, tick.GetInt("Lap"));
    }

    [Fact]
    public void Recording_ClosedUncleanly_CountsRowsFromTheFileLength()
    {
        using var recording = IbtRecording.Open(new MemoryStream(Ibt(50, writeRecordCount: false)));

        Assert.Equal(50, recording.Rows);
    }

    [Fact]
    public void Recording_NotAnIbt_IsRefused()
    {
        Assert.ThrowsAny<Exception>(() => IbtRecording.Open(new MemoryStream(new byte[400])));
    }

    [Fact]
    public void SharedMemoryLayout_IsWhatTheReaderAccepts()
    {
        using var recording = IbtRecording.Open(new MemoryStream(Ibt(10)));
        var layout = IbtReplayer.SharedMemoryLayout.For(recording);

        var image = new byte[layout.TotalSize];
        layout.HeaderBytes(recording).CopyTo(image, 0);
        recording.VarHeaders.CopyTo(image, layout.VarHeaderOffset);
        recording.SessionInfo.CopyTo(image, layout.SessionInfoOffset);
        var header = IrsdkParser.ParseHeader(image);

        Assert.True(header.IsConnected);
        Assert.True(IrsdkParser.IsPlausible(header, layout.TotalSize));
        Assert.Equal(3, header.NumBuf);
        Assert.Equal(Yaml, IrsdkParser.ReadSessionInfoYaml(image, header.SessionInfoOffset, header.SessionInfoLen));
        Assert.Equal(["Speed", "Lap"], IrsdkParser.ParseVarHeaders(image, header.VarHeaderOffset, header.NumVars).Select(v => v.Name));
    }

    /// <summary>Windows only, like the other shared-memory tests: named maps and events.</summary>
    [Fact]
    public void Replay_IsReadByTheConnection_AsIfIRacingWereLive()
    {
        var map = $"Local\\OpenOverlayReplayTest-{Guid.NewGuid():N}";
        var signal = $"Local\\OpenOverlayReplayTestEvent-{Guid.NewGuid():N}";
        using var replayer = new IbtReplayer(IbtRecording.Open(new MemoryStream(Ibt(600))), map, signal);
        var finished = new ManualResetEventSlim();
        replayer.Finished += _ => finished.Set();
        replayer.Start();

        using var connection = new IRacingConnection(map, signal, ConnectionTimings.Default);
        connection.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && (connection.Latest?.GetInt("Lap") ?? 0) < 30)
        {
            Thread.Sleep(20);
        }

        Assert.Equal("spa up", connection.Session?.WeekendInfo?.TrackName);
        var latest = connection.Latest!;
        Assert.Equal(latest.GetInt("Lap") * 2f, latest.GetFloat("Speed"));

        replayer.Stop();
        Assert.True(finished.Wait(TimeSpan.FromSeconds(2)));
    }
}
