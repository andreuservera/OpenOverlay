using IRacingOverlay.App.ViewModels;
using IRacingOverlay.Sdk.Interop;
using IRacingOverlay.Sdk.Tests;

namespace IRacingOverlay.App.Tests;

public class TableConditionsTests
{
    [Fact]
    public void Build_ReadsBrakeBiasAndConditions()
    {
        var builder = new SyntheticMemoryBuilder();
        builder.AddVar("dcBrakeBias", IrsdkVarType.Float);
        builder.AddVar("AirTemp", IrsdkVarType.Float);
        builder.AddVar("TrackTempCrew", IrsdkVarType.Float);
        builder.AddVar("RelativeHumidity", IrsdkVarType.Float);
        var snapshot = TestSnapshotFactory.Build(builder, w =>
        {
            w.SetFloat("dcBrakeBias", 54.5f);
            w.SetFloat("AirTemp", 21.4f);
            w.SetFloat("TrackTempCrew", 33.8f);
            w.SetFloat("RelativeHumidity", 0.54f);
        });

        var conditions = TableConditions.Build(snapshot);

        Assert.Equal("54.5%", conditions.BrakeBiasDisplay);
        Assert.Equal("54%", conditions.HumidityDisplay);
        Assert.StartsWith("21.4", conditions.AirTempDisplay);
        Assert.StartsWith("33.8", conditions.TrackTempDisplay);
    }

    [Fact]
    public void Build_ACarWithoutABrakeBiasAdjuster_ShowsADash()
    {
        var snapshot = TestSnapshotFactory.Build(new SyntheticMemoryBuilder(), _ => { });

        var conditions = TableConditions.Build(snapshot);

        Assert.Equal("—", conditions.BrakeBiasDisplay);
        Assert.Equal("—", conditions.AirTempDisplay);
        Assert.Equal("—", conditions.HumidityDisplay);
    }

    [Fact]
    public void NewElements_StartHidden_SoATableSetUpBeforeKeepsItsLook()
    {
        var options = new DriverTableOptions(DriverTable.Standings);

        foreach (var element in new[] { TableInfoElement.BrakeBias, TableInfoElement.AirTemp, TableInfoElement.TrackTemp, TableInfoElement.Humidity })
        {
            Assert.False(options.IsShown(element));
            Assert.False(DriverTableOptions.HasOwnSwitch(element));
        }

        Assert.False(options.ShowClassDrivers);
        Assert.False(options.ShowClassSof);
        Assert.False(options.ShowFooter && options.IsShown(TableInfoElement.BrakeBias));

        options.SetShown(TableInfoElement.BrakeBias, true);
        Assert.True(options.IsShown(TableInfoElement.BrakeBias));
        Assert.True(options.ShowTopInfo);
    }

    [Fact]
    public void ClassHeaders_CountEveryDriverInTheClass_AndCarryItsOwnSof()
    {
        var rows = new List<StandingsRow>
        {
            Row(0, classId: 1, iRating: 3000, player: true),
            Row(1, classId: 2, iRating: 2000),
            Row(2, classId: 1, iRating: 1000),
            Row(3, classId: 1, iRating: 0),
        };

        var headers = StandingsBuilder.GroupForDisplay(rows).OfType<StandingsHeaderRow>().ToList();

        var gt3 = headers.Single(header => header.ClassName == "GT3");
        Assert.Equal(3, gt3.DriverCount);
        // Unrated drivers count as drivers but not towards the SOF.
        Assert.Equal(StandingsBuilder.StrengthOf([3000, 1000]), gt3.Sof, 6);
        Assert.Equal(1, headers.Single(header => header.ClassName == "GT4").DriverCount);
    }

    private static StandingsRow Row(int carIdx, int classId, int iRating, bool player = false) => new()
    {
        CarIdx = carIdx,
        Position = carIdx + 1,
        ClassPosition = 1,
        Name = $"Driver {carIdx}",
        CarNumber = $"{carIdx}",
        IsPlayer = player,
        OnPitRoad = false,
        CurrentLap = 1,
        GapToLeaderSeconds = 0,
        LastLapTime = 0,
        BestLapTime = 0,
        IsMultiClass = true,
        IRating = iRating,
        LicString = "",
        IRatingDelta = 0,
        IsSessionFastestLap = false,
        CarClassID = classId,
        CarClassName = classId == 1 ? "GT3 Class" : "GT4",
    };
}
