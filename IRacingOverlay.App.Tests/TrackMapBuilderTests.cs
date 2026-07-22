using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class TrackMapBuilderTests
{
    private static SyntheticMemoryBuilder TrackMapVars(int count = 4)
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("CarIdxLapDistPct", IrsdkVarType.Float, count: count);
        builder.AddVar("CarIdxOnPitRoad", IrsdkVarType.Bool, count: count);
        return builder;
    }

    [Fact]
    public void Build_PlacesEachCarAtItsLapDistPct()
    {
        var builder = TrackMapVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
            w.SetFloatArray("CarIdxLapDistPct", [0.1f, 0.5f, 0.9f, 0]));

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Mid", CarNumber = "12" },
                    new DriverEntry { CarIdx = 2, UserName = "Ahead", CarNumber = "42" },
                ],
            },
        };

        var markers = TrackMapBuilder.Build(snapshot, session);

        Assert.Equal(0.1, markers.Single(m => m.CarIdx == 0).LapDistPct, precision: 3);
        Assert.Equal(0.5, markers.Single(m => m.CarIdx == 1).LapDistPct, precision: 3);
        Assert.Equal(0.9, markers.Single(m => m.CarIdx == 2).LapDistPct, precision: 3);
        Assert.True(markers.Single(m => m.CarIdx == 0).IsPlayer);
        Assert.False(markers.Single(m => m.CarIdx == 1).IsPlayer);
    }

    [Fact]
    public void Build_ExcludesCarsWithNoValidPosition()
    {
        // -1 is iRacing's own "no valid position" sentinel (car not out on track this session).
        var builder = TrackMapVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
            w.SetFloatArray("CarIdxLapDistPct", [0.1f, -1f, 0, 0]));

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "NotOut", CarNumber = "9" },
                ],
            },
        };

        var markers = TrackMapBuilder.Build(snapshot, session);

        Assert.Single(markers);
        Assert.DoesNotContain(markers, m => m.CarIdx == 1);
    }

    [Fact]
    public void Build_IgnoresPaceCar()
    {
        var builder = TrackMapVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
            w.SetFloatArray("CarIdxLapDistPct", [0.1f, 0.2f, 0, 0]));

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Pace Car", CarIsPaceCar = 1 },
                ],
            },
        };

        var markers = TrackMapBuilder.Build(snapshot, session);

        Assert.Single(markers);
    }

    [Fact]
    public void Build_SortsMarkersByTrackPosition()
    {
        var builder = TrackMapVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
            w.SetFloatArray("CarIdxLapDistPct", [0.9f, 0.1f, 0.5f, 0]));

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Ahead", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Behind", CarNumber = "12" },
                    new DriverEntry { CarIdx = 2, UserName = "Mid", CarNumber = "42" },
                ],
            },
        };

        var markers = TrackMapBuilder.Build(snapshot, session);

        Assert.Equal([1, 2, 0], markers.Select(m => m.CarIdx));
    }

    [Fact]
    public void Build_MarksOnPitRoad()
    {
        var builder = TrackMapVars();
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloatArray("CarIdxLapDistPct", [0.1f, 0.2f, 0, 0]);
            w.SetBoolArray("CarIdxOnPitRoad", [false, true, false, false]);
        });

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection
            {
                DriverCarIdx = 0,
                Drivers =
                [
                    new DriverEntry { CarIdx = 0, UserName = "Me", CarNumber = "7" },
                    new DriverEntry { CarIdx = 1, UserName = "Pitting", CarNumber = "9" },
                ],
            },
        };

        var markers = TrackMapBuilder.Build(snapshot, session);

        Assert.True(markers.Single(m => m.CarIdx == 1).OnPitRoad);
        Assert.False(markers.Single(m => m.CarIdx == 0).OnPitRoad);
    }

    [Fact]
    public void Build_MissingVariable_ReturnsEmpty()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("Speed", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloat("Speed", 10));

        var session = new IracingSessionInfo
        {
            DriverInfo = new DriverInfoSection { Drivers = [new DriverEntry { CarIdx = 0, CarNumber = "7" }] },
        };

        var markers = TrackMapBuilder.Build(snapshot, session);

        Assert.Empty(markers);
    }

    [Fact]
    public void Build_NoSession_ReturnsEmpty()
    {
        var builder = TrackMapVars();
        var snapshot = TestSnapshotFactory.Build(builder, w => w.SetFloatArray("CarIdxLapDistPct", [0.1f, 0, 0, 0]));

        var markers = TrackMapBuilder.Build(snapshot, session: null);

        Assert.Empty(markers);
    }
}
