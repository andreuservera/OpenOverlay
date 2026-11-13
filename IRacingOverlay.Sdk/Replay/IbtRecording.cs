using System.Buffers.Binary;
using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk.Replay;

/// <summary>
/// An .ibt telemetry file, read the way iRacing's shared memory is laid out: an .ibt starts with the
/// same irsdk_header, followed by a disk sub-header (session times, lap and record counts), the same
/// variable headers, the session YAML, and then one row per tick, each row exactly one shared-memory
/// tick buffer. That is what lets a recording be played back into the overlays byte for byte.
/// </summary>
public sealed class IbtRecording : IDisposable
{
    // irsdk_diskSubHeader follows the 112-byte header: sessionStartDate (8), sessionStartTime (8),
    // sessionEndTime (8), sessionLapCount (4), sessionRecordCount (4).
    private const int RecordCountOffset = IrsdkConstants.HeaderTotalSize + 28;
    private const int DiskSubHeaderEnd = IrsdkConstants.HeaderTotalSize + 32;

    private readonly Stream _stream;
    private readonly long _firstRow;

    private IbtRecording(Stream stream, int tickRate, int numVars, int bufLen, byte[] varHeaders, byte[] sessionInfo, long firstRow, int rows)
    {
        _stream = stream;
        TickRate = tickRate;
        NumVars = numVars;
        BufLen = bufLen;
        VarHeaders = varHeaders;
        SessionInfo = sessionInfo;
        _firstRow = firstRow;
        Rows = rows;
    }

    /// <summary>Rows per second; 60 for every recording iRacing writes.</summary>
    public int TickRate { get; }

    public int NumVars { get; }

    /// <summary>Bytes in one row, the same as one shared-memory tick buffer.</summary>
    public int BufLen { get; }

    /// <summary>The variable header table, as stored (NumVars × 144 bytes).</summary>
    public byte[] VarHeaders { get; }

    /// <summary>The session YAML, as stored.</summary>
    public byte[] SessionInfo { get; }

    public int Rows { get; }

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Rows / TickRate);

    public static IbtRecording Open(string path) =>
        Open(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.RandomAccess));

    /// <summary>Reads the headers; the rows are read one at a time as they're asked for.</summary>
    public static IbtRecording Open(Stream stream)
    {
        try
        {
            var head = new byte[DiskSubHeaderEnd];
            stream.Position = 0;
            stream.ReadExactly(head);
            var header = IrsdkParser.ParseHeader(head);
            var rowsField = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(RecordCountOffset));
            if (header.NumVars <= 0 || header.BufLen <= 0 || header.VarBufs.Count == 0 || header.VarHeaderOffset < IrsdkConstants.HeaderTotalSize)
            {
                throw new InvalidDataException("Not an iRacing telemetry file.");
            }

            var varHeaders = new byte[header.NumVars * IrsdkConstants.VarHeaderEntrySize];
            stream.Position = header.VarHeaderOffset;
            stream.ReadExactly(varHeaders);

            var sessionInfo = new byte[Math.Max(0, header.SessionInfoLen)];
            stream.Position = header.SessionInfoOffset;
            stream.ReadExactly(sessionInfo);
            // The YAML is zero-padded on disk; the reader stops at the first zero anyway.
            var end = Array.IndexOf(sessionInfo, (byte)0);
            if (end >= 0)
            {
                Array.Resize(ref sessionInfo, end);
            }

            long firstRow = header.VarBufs[0].BufOffset;
            // The record count is only written when iRacing closes the file cleanly; otherwise trust
            // the file's length.
            var rowsInFile = (int)Math.Max(0, (stream.Length - firstRow) / header.BufLen);
            var rows = rowsField > 0 ? Math.Min(rowsField, rowsInFile) : rowsInFile;
            var tickRate = header.TickRate > 0 ? header.TickRate : 60;
            return new IbtRecording(stream, tickRate, header.NumVars, header.BufLen, varHeaders, sessionInfo, firstRow, rows);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Copies one row into <paramref name="destination"/> (at least <see cref="BufLen"/> bytes).</summary>
    public void ReadRow(int index, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Rows);
        _stream.Position = _firstRow + (long)index * BufLen;
        _stream.ReadExactly(destination[..BufLen]);
    }

    public void Dispose() => _stream.Dispose();
}
