using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

/// <summary>Builds a TelemetrySnapshot from a synthetic memory image, for exercising App-layer
/// view-model builders (StandingsBuilder, CockpitBuilder) without a live iRacing session.</summary>
internal static class TestSnapshotFactory
{
    public static TelemetrySnapshot Build(SyntheticMemoryBuilder builder, Action<SyntheticMemoryBuilder.TickBufferWriter> writeValues)
    {
        var bufLen = builder.TotalSize;
        var data = builder.BuildTickBuffer(bufLen, writeValues);
        var varsByName = builder.BuildVarsByName();
        return new TelemetrySnapshot(data, varsByName, tickCount: 1);
    }
}
