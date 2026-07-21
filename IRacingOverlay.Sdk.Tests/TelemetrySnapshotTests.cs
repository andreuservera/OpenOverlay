using IRacingOverlay.Sdk.Interop;

namespace IRacingOverlay.Sdk.Tests;

public class TelemetrySnapshotTests
{
    private static TelemetrySnapshot BuildSnapshot(out SyntheticMemoryBuilder builder)
    {
        builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        builder.AddVar("Lap", IrsdkVarType.Int);
        builder.AddVar("Fuel", IrsdkVarType.Double);
        builder.AddVar("IsOnTrack", IrsdkVarType.Bool);
        builder.AddVar("EngineWarnings", IrsdkVarType.BitField);
        builder.AddVar("CarIdxLap", IrsdkVarType.Int, count: 3);
        builder.AddVar("CarIdxLapDistPct", IrsdkVarType.Float, count: 3);
        builder.AddVar("CarIdxOnPitRoad", IrsdkVarType.Bool, count: 3);

        var bufLen = 4 + 4 + 8 + 1 + 4 + 3 * 4 + 3 * 4 + 3;
        var data = builder.BuildTickBuffer(bufLen, w =>
        {
            w.SetFloat("Speed", 55.5f);
            w.SetInt("Lap", 12);
            w.SetDouble("Fuel", 24.75);
            w.SetBool("IsOnTrack", true);
            w.SetBitField("EngineWarnings", 0b101);
            w.SetIntArray("CarIdxLap", [12, 11, 12]);
            w.SetFloatArray("CarIdxLapDistPct", [0.5f, 0.9f, 0.1f]);
            w.SetBoolArray("CarIdxOnPitRoad", [false, true, false]);
        });

        var varsByName = builder.BuildVarsByName();
        return new TelemetrySnapshot(data, varsByName, tickCount: 1);
    }

    [Fact]
    public void ScalarGetters_ReturnValuesWrittenIntoBuffer()
    {
        var snapshot = BuildSnapshot(out _);

        Assert.Equal(55.5f, snapshot.GetFloat("Speed"));
        Assert.Equal(12, snapshot.GetInt("Lap"));
        Assert.Equal(24.75, snapshot.GetDouble("Fuel"));
        Assert.True(snapshot.GetBool("IsOnTrack"));
        Assert.Equal(0b101u, snapshot.GetBitField("EngineWarnings"));
    }

    [Fact]
    public void ArrayGetters_ReturnAllElementsInOrder()
    {
        var snapshot = BuildSnapshot(out _);

        Assert.Equal([12, 11, 12], snapshot.GetIntArray("CarIdxLap"));
        Assert.Equal([0.5f, 0.9f, 0.1f], snapshot.GetFloatArray("CarIdxLapDistPct"));
        Assert.Equal([false, true, false], snapshot.GetBoolArray("CarIdxOnPitRoad"));
    }

    [Fact]
    public void GetInt_UnknownVariable_ThrowsArgumentException()
    {
        var snapshot = BuildSnapshot(out _);

        Assert.Throws<ArgumentException>(() => snapshot.GetInt("DoesNotExist"));
    }

    [Fact]
    public void GetInt_WrongType_ThrowsInvalidOperationException()
    {
        var snapshot = BuildSnapshot(out _);

        Assert.Throws<InvalidOperationException>(() => snapshot.GetInt("Speed"));
    }

    [Fact]
    public void HasVariable_ReflectsPresence()
    {
        var snapshot = BuildSnapshot(out _);

        Assert.True(snapshot.HasVariable("Speed"));
        Assert.False(snapshot.HasVariable("Nope"));
    }
}
