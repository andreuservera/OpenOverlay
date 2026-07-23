using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk;

/// <summary>
/// A single tick's worth of telemetry data, with typed lookups by iRacing variable name
/// (e.g. "Speed", "CarIdxLapDistPct"). Immutable snapshot — safe to hand to UI code and read at leisure.
/// </summary>
public sealed class TelemetrySnapshot
{
    private readonly byte[] _data;
    private readonly IReadOnlyDictionary<string, IrsdkVarHeader> _varsByName;

    // A fresh TelemetrySnapshot instance is handed out once per tick and never mutated afterward, but
    // several independent Builders each ask for the same per-car array (e.g. CarIdxEstTime is read by
    // Cockpit, Relative, Standings, and TrackMap builders every tick) — without this cache, every one
    // of those call sites re-parsed and re-allocated its own full copy of the array from raw bytes,
    // several times per tick. Caching by variable name here means each array is parsed once per tick
    // no matter how many builders ask for it, since it's scoped to (and discarded with) this instance.
    private Dictionary<string, Array>? _arrayCache;

    internal TelemetrySnapshot(byte[] data, IReadOnlyDictionary<string, IrsdkVarHeader> varsByName, int tickCount)
    {
        _data = data;
        _varsByName = varsByName;
        TickCount = tickCount;
    }

    public int TickCount { get; }

    public bool HasVariable(string name) => _varsByName.ContainsKey(name);

    public int GetInt(string name)
    {
        var header = ResolveHeader(name, IrsdkVarType.Int);
        return BitConverter.ToInt32(_data, header.Offset);
    }

    public float GetFloat(string name)
    {
        var header = ResolveHeader(name, IrsdkVarType.Float);
        return BitConverter.ToSingle(_data, header.Offset);
    }

    public double GetDouble(string name)
    {
        var header = ResolveHeader(name, IrsdkVarType.Double);
        return BitConverter.ToDouble(_data, header.Offset);
    }

    public bool GetBool(string name)
    {
        var header = ResolveHeader(name, IrsdkVarType.Bool);
        return _data[header.Offset] != 0;
    }

    public uint GetBitField(string name)
    {
        var header = ResolveHeader(name, IrsdkVarType.BitField);
        return BitConverter.ToUInt32(_data, header.Offset);
    }

    public int[] GetIntArray(string name) => GetArray(name, IrsdkVarType.Int, 4, BitConverter.ToInt32);

    public float[] GetFloatArray(string name) => GetArray(name, IrsdkVarType.Float, 4, BitConverter.ToSingle);

    public bool[] GetBoolArray(string name) => GetArray(name, IrsdkVarType.Bool, 1, (data, offset) => data[offset] != 0);

    private T[] GetArray<T>(string name, IrsdkVarType expectedType, int elementSize, Func<byte[], int, T> convert)
    {
        if (_arrayCache is not null && _arrayCache.TryGetValue(name, out var cached))
        {
            return (T[])cached;
        }

        var header = ResolveHeader(name, expectedType);
        var result = new T[header.Count];
        for (var i = 0; i < header.Count; i++)
        {
            result[i] = convert(_data, header.Offset + i * elementSize);
        }

        (_arrayCache ??= new Dictionary<string, Array>())[name] = result;
        return result;
    }

    private IrsdkVarHeader ResolveHeader(string name, IrsdkVarType expectedType)
    {
        if (!_varsByName.TryGetValue(name, out var header))
        {
            throw new ArgumentException($"Unknown telemetry variable '{name}'.", nameof(name));
        }

        if (header.Type != expectedType)
        {
            throw new InvalidOperationException(
                $"Telemetry variable '{name}' is of type {header.Type}, not {expectedType}.");
        }

        return header;
    }
}
