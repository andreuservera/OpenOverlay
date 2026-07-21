using System.Text;
using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk.Tests;

/// <summary>
/// Builds a synthetic byte image that mimics iRacing's real shared-memory layout, so the
/// binary parsing code can be exercised without a live iRacing session.
/// </summary>
public sealed class SyntheticMemoryBuilder
{
    public sealed class VarDef
    {
        public required string Name;
        public required IrsdkVarType Type;
        public int Count = 1;
        public string Unit = "";
        public string Desc = "";
        public int Offset; // filled in by Build()
    }

    private readonly List<VarDef> _vars = [];
    private int _cursor;

    /// <summary>Total bytes a tick buffer needs to hold every var declared so far via AddVar.</summary>
    public int TotalSize => _cursor;

    public VarDef AddVar(string name, IrsdkVarType type, int count = 1, string unit = "", string desc = "")
    {
        var def = new VarDef { Name = name, Type = type, Count = count, Unit = unit, Desc = desc, Offset = _cursor };
        _cursor += ElementSize(type) * count;
        _vars.Add(def);
        return def;
    }

    private static int ElementSize(IrsdkVarType type) => type switch
    {
        IrsdkVarType.Char => 1,
        IrsdkVarType.Bool => 1,
        IrsdkVarType.Int => 4,
        IrsdkVarType.BitField => 4,
        IrsdkVarType.Float => 4,
        IrsdkVarType.Double => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>
    /// Lays out one tick buffer's raw bytes (not including the header/var-header tables) using
    /// the var offsets computed by Build(). Values are supplied as raw setter callbacks.
    /// </summary>
    public byte[] BuildTickBuffer(int bufLen, Action<TickBufferWriter> writeValues)
    {
        var buffer = new byte[bufLen];
        var writer = new TickBufferWriter(buffer, _vars);
        writeValues(writer);
        return buffer;
    }

    public sealed class TickBufferWriter(byte[] buffer, List<VarDef> vars)
    {
        public void SetInt(string name, int value) => WriteAt(name, BitConverter.GetBytes(value));
        public void SetFloat(string name, float value) => WriteAt(name, BitConverter.GetBytes(value));
        public void SetDouble(string name, double value) => WriteAt(name, BitConverter.GetBytes(value));
        public void SetBool(string name, bool value) => buffer[Find(name).Offset] = (byte)(value ? 1 : 0);
        public void SetBitField(string name, uint value) => WriteAt(name, BitConverter.GetBytes(value));

        public void SetFloatArray(string name, float[] values)
        {
            var def = Find(name);
            for (var i = 0; i < values.Length; i++)
            {
                BitConverter.GetBytes(values[i]).CopyTo(buffer, def.Offset + i * 4);
            }
        }

        public void SetIntArray(string name, int[] values)
        {
            var def = Find(name);
            for (var i = 0; i < values.Length; i++)
            {
                BitConverter.GetBytes(values[i]).CopyTo(buffer, def.Offset + i * 4);
            }
        }

        public void SetBoolArray(string name, bool[] values)
        {
            var def = Find(name);
            for (var i = 0; i < values.Length; i++)
            {
                buffer[def.Offset + i] = (byte)(values[i] ? 1 : 0);
            }
        }

        private void WriteAt(string name, byte[] bytes) => bytes.CopyTo(buffer, Find(name).Offset);

        private VarDef Find(string name) => vars.First(v => v.Name == name);
    }

    /// <summary>
    /// Assembles a full shared-memory image: header + var-header table + session info YAML +
    /// N tick buffers (each supplied pre-built via <see cref="BuildTickBuffer"/>).
    /// </summary>
    public byte[] BuildFullImage(
        int tickRate,
        bool connected,
        string sessionInfoYaml,
        int bufLen,
        IReadOnlyList<(int tickCount, byte[] data)> tickBuffers)
    {
        var varHeaderOffset = IrsdkConstants.HeaderTotalSize;
        var varHeaderTableSize = _vars.Count * IrsdkConstants.VarHeaderEntrySize;
        var sessionInfoOffset = varHeaderOffset + varHeaderTableSize;
        var sessionInfoBytes = Encoding.UTF8.GetBytes(sessionInfoYaml);
        var firstBufOffset = sessionInfoOffset + sessionInfoBytes.Length;

        var totalSize = firstBufOffset + tickBuffers.Count * bufLen;
        var image = new byte[totalSize];

        WriteInt32(image, IrsdkConstants.HeaderVerOffset, 2);
        WriteInt32(image, IrsdkConstants.HeaderStatusOffset, connected ? IrsdkConstants.StatusConnected : 0);
        WriteInt32(image, IrsdkConstants.HeaderTickRateOffset, tickRate);
        WriteInt32(image, IrsdkConstants.HeaderSessionInfoUpdateOffset, 1);
        WriteInt32(image, IrsdkConstants.HeaderSessionInfoLenOffset, sessionInfoBytes.Length);
        WriteInt32(image, IrsdkConstants.HeaderSessionInfoOffsetOffset, sessionInfoOffset);
        WriteInt32(image, IrsdkConstants.HeaderNumVarsOffset, _vars.Count);
        WriteInt32(image, IrsdkConstants.HeaderVarHeaderOffsetOffset, varHeaderOffset);
        WriteInt32(image, IrsdkConstants.HeaderNumBufOffset, tickBuffers.Count);
        WriteInt32(image, IrsdkConstants.HeaderBufLenOffset, bufLen);

        for (var i = 0; i < tickBuffers.Count; i++)
        {
            var entryOffset = IrsdkConstants.HeaderVarBufArrayOffset + i * IrsdkConstants.VarBufEntrySize;
            var bufOffset = firstBufOffset + i * bufLen;
            WriteInt32(image, entryOffset, tickBuffers[i].tickCount);
            WriteInt32(image, entryOffset + 4, bufOffset);
            tickBuffers[i].data.CopyTo(image, bufOffset);
        }

        for (var i = 0; i < _vars.Count; i++)
        {
            var v = _vars[i];
            var entryOffset = varHeaderOffset + i * IrsdkConstants.VarHeaderEntrySize;
            WriteInt32(image, entryOffset + IrsdkConstants.VarHeaderTypeOffset, (int)v.Type);
            WriteInt32(image, entryOffset + IrsdkConstants.VarHeaderOffsetOffset, v.Offset);
            WriteInt32(image, entryOffset + IrsdkConstants.VarHeaderCountOffset, v.Count);
            WriteAsciiFixed(image, entryOffset + IrsdkConstants.VarHeaderNameOffset, v.Name, IrsdkConstants.MaxString);
            WriteAsciiFixed(image, entryOffset + IrsdkConstants.VarHeaderDescOffset, v.Desc, IrsdkConstants.MaxDesc);
            WriteAsciiFixed(image, entryOffset + IrsdkConstants.VarHeaderUnitOffset, v.Unit, IrsdkConstants.MaxString);
        }

        sessionInfoBytes.CopyTo(image, sessionInfoOffset);

        return image;
    }

    public IReadOnlyDictionary<string, IrsdkVarHeader> BuildVarsByName() =>
        _vars.ToDictionary(v => v.Name, v => new IrsdkVarHeader
        {
            Type = v.Type,
            Offset = v.Offset,
            Count = v.Count,
            CountAsTime = false,
            Name = v.Name,
            Description = v.Desc,
            Unit = v.Unit,
        });

    private static void WriteInt32(byte[] buffer, int offset, int value) =>
        BitConverter.GetBytes(value).CopyTo(buffer, offset);

    private static void WriteAsciiFixed(byte[] buffer, int offset, string value, int maxLength)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var len = Math.Min(bytes.Length, maxLength);
        Array.Copy(bytes, 0, buffer, offset, len);
    }
}
