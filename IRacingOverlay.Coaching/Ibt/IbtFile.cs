using IRacingOverlay.Coaching.Telemetry;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Coaching.Ibt;

/// <summary>
/// An iRacing disk-telemetry file (.ibt, written to Documents\iRacing\telemetry after Alt+L).
///
/// Layout, confirmed against real files: the same 112-byte irsdk header as shared memory, then a
/// 32-byte disk sub-header (session start date as time_t, session start/end time, lap count, record
/// count), then the variable headers and session-info YAML at the offsets the header names, then
/// <see cref="RecordCount"/> tick records of <c>BufLen</c> bytes each starting at the first var-buf
/// offset. Header, variables and YAML are parsed with the app's own <see cref="IrsdkParser"/> and
/// <see cref="SessionInfoParser"/>, unchanged.
/// </summary>
public sealed class IbtFile : IDisposable
{
    // Sizes from iRacing's irsdk_defines.h (the Sdk keeps its copies internal).
    private const int DiskSubHeaderOffset = 112; // sizeof(irsdk_header)
    private const int VarHeaderEntrySize = 144; // sizeof(irsdk_varHeader)
    private const int DiskSubHeaderSize = 32;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly long _dataStart;
    private readonly FrameDecoder _decoder;

    private IbtFile(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;

        var head = ReadExactly(0, DiskSubHeaderOffset + DiskSubHeaderSize, "header");
        Header = IrsdkParser.ParseHeader(head);
        if (Header.NumVars <= 0 || Header.BufLen <= 0 || Header.NumBuf < 1)
        {
            throw new InvalidDataException("Not an iRacing telemetry file: the header names no variables.");
        }

        var sub = head.AsSpan(DiskSubHeaderOffset);
        SessionStartUtc = DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(sub[..8])).UtcDateTime;
        SessionStartTime = BitConverter.ToDouble(sub.Slice(8, 8));
        SessionEndTime = BitConverter.ToDouble(sub.Slice(16, 8));
        SessionLapCount = BitConverter.ToInt32(sub.Slice(24, 4));

        var varBytes = ReadExactly(Header.VarHeaderOffset, Header.NumVars * VarHeaderEntrySize, "variable headers");
        VariableList = IrsdkParser.ParseVarHeaders(varBytes, 0, Header.NumVars);
        Variables = IrsdkParser.BuildVarMap(VariableList, Header.BufLen);
        _decoder = new FrameDecoder(Variables);

        if (Header.SessionInfoLen > 0)
        {
            var yamlBytes = ReadExactly(Header.SessionInfoOffset, Header.SessionInfoLen, "session info");
            SessionInfoYaml = IrsdkParser.ReadSessionInfoYaml(yamlBytes, 0, Header.SessionInfoLen);
            Session = SessionInfoParser.Parse(SessionInfoYaml).Session;
        }

        _dataStart = Header.VarBufs[0].BufOffset;
        var declared = BitConverter.ToInt32(sub.Slice(28, 4));
        // A file cut short (iRacing crashed, copy interrupted) still reads up to its last whole record.
        var available = (int)Math.Max(0, (_stream.Length - _dataStart) / Header.BufLen);
        RecordCount = declared > 0 ? Math.Min(declared, available) : available;
    }

    public static IbtFile Open(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan), ownsStream: true);

    public static IbtFile Open(Stream stream, bool leaveOpen = false)
    {
        if (!stream.CanSeek || !stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
        }

        return new IbtFile(stream, ownsStream: !leaveOpen);
    }

    public IrsdkHeader Header { get; }

    /// <summary>Ticks per second the file was recorded at (60 for every file seen so far).</summary>
    public int TickRate => Header.TickRate;

    public DateTime SessionStartUtc { get; }
    public double SessionStartTime { get; }
    public double SessionEndTime { get; }
    public int SessionLapCount { get; }
    public int RecordCount { get; }

    public IReadOnlyList<IrsdkVarHeader> VariableList { get; }
    public IReadOnlyDictionary<string, IrsdkVarHeader> Variables { get; }

    public string? SessionInfoYaml { get; }
    public IracingSessionInfo? Session { get; }

    public bool HasVariable(string name) => Variables.ContainsKey(name);

    /// <summary>Every record as a <see cref="TelemetryFrame"/>, in order. Streams from disk.</summary>
    public IEnumerable<TelemetryFrame> Frames()
    {
        var buffer = new byte[Header.BufLen];
        for (var i = 0; i < RecordCount; i++)
        {
            ReadRecordInto(i, buffer);
            yield return _decoder.Decode(buffer);
        }
    }

    /// <summary>One raw tick record, for reading channels the frame doesn't carry.</summary>
    public IbtRecord ReadRecord(int index)
    {
        var buffer = new byte[Header.BufLen];
        ReadRecordInto(index, buffer);
        return new IbtRecord(buffer, Variables);
    }

    private void ReadRecordInto(int index, byte[] buffer)
    {
        if ((uint)index >= (uint)RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _stream.Position = _dataStart + (long)index * Header.BufLen;
        _stream.ReadExactly(buffer);
    }

    private byte[] ReadExactly(long offset, int length, string what)
    {
        if (offset < 0 || length < 0 || offset + length > _stream.Length)
        {
            throw new InvalidDataException($"Not an iRacing telemetry file: the {what} lie outside the file.");
        }

        var bytes = new byte[length];
        _stream.Position = offset;
        _stream.ReadExactly(bytes);
        return bytes;
    }

    public void Dispose()
    {
        if (_ownsStream)
        {
            _stream.Dispose();
        }
    }
}

/// <summary>One raw .ibt tick, readable by variable name.</summary>
public sealed class IbtRecord
{
    private readonly byte[] _data;
    private readonly IReadOnlyDictionary<string, IrsdkVarHeader> _vars;

    internal IbtRecord(byte[] data, IReadOnlyDictionary<string, IrsdkVarHeader> vars)
    {
        _data = data;
        _vars = vars;
    }

    public bool HasVariable(string name) => _vars.ContainsKey(name);

    /// <summary>Element <paramref name="index"/> of a variable as a double, whatever its type.</summary>
    public double Get(string name, int index = 0)
    {
        if (!_vars.TryGetValue(name, out var header))
        {
            throw new ArgumentException($"Unknown telemetry variable '{name}'.", nameof(name));
        }

        if ((uint)index >= (uint)header.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return FrameDecoder.Read(_data, header, index);
    }
}
